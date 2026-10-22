# Local backup and restore of the development database (container resume-dev-pg), story 4.3.
#   backup   pg_dump -Fc of the source database into backups/<db>-<timestamp>-<id>.dump
#   restore  re-creates the target database (DROP + CREATE) and pg_restore the given file into it
#   verify   backup, restore into the target database, compare row counts with the source, then drop the target
# Usage: powershell -File scripts/backup-restore.ps1 <backup|restore|verify> [<file>] [-Force]
# The password is never printed. It comes from -Password, else $env:PGPASSWORD, else the dev default.
# -Force only overrides two refusals: a target with active connections, and a target whose name does not start with resume_restore.
# A target equal to the source, or one of the protected names, is never restored into.
param(
  [Parameter(Position = 0)][string]$Action,
  [Parameter(Position = 1)][string]$File,
  [string]$Container = 'resume-dev-pg',
  [string]$SourceDatabase = 'resume',
  [string]$TargetDatabase = 'resume_restore_check',
  [string]$User = 'postgres',
  [string]$Password,
  [string]$BackupDir,
  [switch]$Force
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
if (-not $Password) { $Password = if ($env:PGPASSWORD) { $env:PGPASSWORD } else { 'dev' } }
if (-not $BackupDir) { $BackupDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'backups' }
$Protected = @('resume', 'postgres', 'template0', 'template1', 'resume_e2e')
$CountTables = @('profiles', 'profile_revisions', 'participant_private', 'cohorts', 'audit_log')

# Throws so that finally blocks (temp file and database cleanup) always run; the top level prints it and exits 1.
function Fail([string]$Message) { throw $Message }

# Runs docker with the password passed through the environment (docker exec -e NAME copies it, so it is not on the command line).
function Invoke-Docker([string[]]$DockerArgs) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'docker'
  $psi.Arguments = ($DockerArgs | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.UseShellExecute = $false
  $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
  $psi.EnvironmentVariables['PGPASSWORD'] = $Password
  try { $p = [System.Diagnostics.Process]::Start($psi) } catch { Fail 'הפקודה docker אינה זמינה.' }
  $outTask = $p.StandardOutput.ReadToEndAsync()
  $err = $p.StandardError.ReadToEnd()
  $p.WaitForExit()
  [pscustomobject]@{ ExitCode = $p.ExitCode; Out = $outTask.Result.Trim(); Err = $err.Trim() }
}

function Invoke-Psql([string]$Database, [string]$Sql) {
  Invoke-Docker @('exec', '-e', 'PGPASSWORD', $Container, 'psql', '-U', $User, '-d', $Database, '-X', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-c', $Sql)
}

function Assert-ContainerRunning {
  $r = Invoke-Docker @('inspect', '-f', '{{.State.Running}}', $Container)
  if ($r.ExitCode -ne 0 -or $r.Out -ne 'true') { Fail "הקונטיינר $Container אינו רץ. הפעילי אותו (docker start $Container) ונסי שוב." }
}

function Assert-ValidName([string]$Name) {
  if ($Name -notmatch '^[a-z_][a-z0-9_]*$') { Fail "שם מסד לא תקין: $Name (אותיות קטנות באנגלית, ספרות וקו תחתון בלבד)." }
}

# Everything that can be refused without touching docker.
function Assert-TargetAllowed {
  Assert-ValidName $TargetDatabase
  Assert-ValidName $SourceDatabase
  if ($TargetDatabase -eq $SourceDatabase) { Fail "מסד היעד ($TargetDatabase) זהה למסד המקור. לא משחזרים על מסד חי." }
  if ($Protected -contains $TargetDatabase) { Fail "מסד היעד ($TargetDatabase) הוא מסד מוגן ($($Protected -join ', ')). בחרי שם אחר." }
  if (-not $TargetDatabase.StartsWith('resume_restore') -and -not $Force) {
    Fail "שם מסד היעד ($TargetDatabase) אינו מתחיל ב-resume_restore. אם זו כוונתך, הריצי עם -Force."
  }
}

function Get-Counts([string]$Database) {
  $counts = [ordered]@{}
  foreach ($t in $CountTables) {
    $r = Invoke-Psql $Database "SELECT count(*) FROM $t"
    if ($r.ExitCode -ne 0) { Fail "ספירת השורות בטבלה $t במסד $Database נכשלה: $($r.Err)" }
    $counts[$t] = $r.Out
  }
  $counts
}

function Invoke-Backup {
  Assert-ValidName $SourceDatabase
  Assert-ContainerRunning
  New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
  $name = '{0}-{1}-{2}.dump' -f $SourceDatabase, (Get-Date -Format 'yyyyMMdd-HHmmss'), [guid]::NewGuid().ToString('N').Substring(0, 8)
  $local = Join-Path $BackupDir $name
  $tmp = "/tmp/$name"
  $ok = $false
  try {
    $r = Invoke-Docker @('exec', '-e', 'PGPASSWORD', $Container, 'pg_dump', '-U', $User, '-d', $SourceDatabase, '-Fc', '-f', $tmp)
    if ($r.ExitCode -ne 0) { Fail "pg_dump נכשל: $($r.Err)" }
    $r = Invoke-Docker @('cp', "${Container}:$tmp", $local)
    if ($r.ExitCode -ne 0 -or -not (Test-Path $local) -or (Get-Item $local).Length -eq 0) { Fail "העתקת הגיבוי מהקונטיינר נכשלה: $($r.Err)" }
    $ok = $true
  }
  finally {
    Invoke-Docker @('exec', $Container, 'rm', '-f', $tmp) | Out-Null
    if (-not $ok -and (Test-Path $local) -and (Get-Item $local).Length -eq 0) { Remove-Item $local -Force }
  }
  Write-Host "הגיבוי נשמר: $local"
  return $local
}

function Invoke-Restore([string]$DumpFile) {
  if (-not $DumpFile) { Fail 'חסר קובץ גיבוי. שימוש: backup-restore.ps1 restore <קובץ>' }
  if (-not (Test-Path -LiteralPath $DumpFile -PathType Leaf)) { Fail "קובץ הגיבוי לא נמצא: $DumpFile" }
  Assert-TargetAllowed
  Assert-ContainerRunning
  $active = Invoke-Psql 'postgres' "SELECT count(*) FROM pg_stat_activity WHERE datname = '$TargetDatabase' AND pid <> pg_backend_pid()"
  if ($active.ExitCode -ne 0) { Fail "בדיקת חיבורים פעילים נכשלה: $($active.Err)" }
  if ([int]$active.Out -gt 0 -and -not $Force) { Fail "למסד $TargetDatabase יש $($active.Out) חיבורים פעילים. סגרי אותם, או הריצי עם -Force כדי לנתק ולמחוק." }

  $tmp = '/tmp/restore-{0}.dump' -f [guid]::NewGuid().ToString('N')
  $dropSql = if ($Force) { "DROP DATABASE IF EXISTS $TargetDatabase WITH (FORCE)" } else { "DROP DATABASE IF EXISTS $TargetDatabase" }
  $created = $false
  $ok = $false
  try {
    $r = Invoke-Docker @('cp', (Resolve-Path -LiteralPath $DumpFile).Path, "${Container}:$tmp")
    if ($r.ExitCode -ne 0) { Fail "העתקת הקובץ לקונטיינר נכשלה: $($r.Err)" }
    $r = Invoke-Docker @('exec', $Container, 'pg_restore', '--list', $tmp)
    if ($r.ExitCode -ne 0) { Fail "קובץ הגיבוי פגום או אינו בפורמט pg_dump -Fc: $($r.Err)" }
    $r = Invoke-Psql 'postgres' $dropSql
    if ($r.ExitCode -ne 0) { Fail "מחיקת מסד היעד נכשלה: $($r.Err)" }
    $r = Invoke-Psql 'postgres' "CREATE DATABASE $TargetDatabase"
    if ($r.ExitCode -ne 0) { Fail "יצירת מסד היעד נכשלה: $($r.Err)" }
    $created = $true
    $r = Invoke-Docker @('exec', '-e', 'PGPASSWORD', $Container, 'pg_restore', '-U', $User, '-d', $TargetDatabase, '--no-owner', '--exit-on-error', $tmp)
    if ($r.ExitCode -ne 0) { Fail "pg_restore נכשל: $($r.Err)" }
    $ok = $true
  }
  finally {
    Invoke-Docker @('exec', $Container, 'rm', '-f', $tmp) | Out-Null
    # A half-restored database holds personal data: remove it (we created it a moment ago).
    if ($created -and -not $ok) { Invoke-Psql 'postgres' "DROP DATABASE IF EXISTS $TargetDatabase WITH (FORCE)" | Out-Null }
  }
  Write-Host "השחזור הושלם אל המסד $TargetDatabase."
}

try {
  switch ($Action) {
    'backup' { Invoke-Backup | Out-Null }
    'restore' { Invoke-Restore $File }
    'verify' {
      Assert-TargetAllowed
      Assert-ContainerRunning
      $sourceCounts = Get-Counts $SourceDatabase
      $dump = Invoke-Backup | Select-Object -Last 1
      $restored = $false
      try {
        Invoke-Restore $dump
        $restored = $true
        $restoredCounts = Get-Counts $TargetDatabase
        $migrations = Invoke-Psql $TargetDatabase 'SELECT count(*) FROM "__EFMigrationsHistory"'
        if ($migrations.ExitCode -ne 0) { Fail "בדיקת היסטוריית המיגרציות נכשלה: $($migrations.Err)" }
        foreach ($t in $CountTables) {
          if ($sourceCounts[$t] -ne $restoredCounts[$t]) { Fail "אי התאמה בטבלה $t : במקור $($sourceCounts[$t]) שורות, במשוחזר $($restoredCounts[$t])." }
        }
        $summary = ($CountTables | ForEach-Object { "$_=$($restoredCounts[$_])" }) -join ', '
        Write-Host "אומת: $($migrations.Out) מיגרציות, והספירות זהות למקור ($summary)."
      }
      finally {
        # The restored database holds personal data: always drop it.
        if ($restored) {
          $r = Invoke-Psql 'postgres' "DROP DATABASE IF EXISTS $TargetDatabase WITH (FORCE)"
          if ($r.ExitCode -ne 0) { [Console]::Error.WriteLine("אזהרה: ניקוי מסד היעד $TargetDatabase נכשל: $($r.Err)") }
          else { Write-Host "מסד היעד $TargetDatabase נמחק. קובץ הגיבוי נשמר: $dump" }
        }
      }
    }
    default { Fail 'פעולה לא מוכרת. שימוש: backup-restore.ps1 <backup|restore|verify> [<קובץ>] [-Force]' }
  }
}
catch {
  [Console]::Error.WriteLine("שגיאה: $($_.Exception.Message)")
  exit 1
}
exit 0
