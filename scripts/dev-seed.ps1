# Seeds sample gallery data into the dev database (container resume-dev-pg, database resume).
# Idempotent; only touches rows with the fixed seed ids. Usage: powershell -File scripts/dev-seed.ps1
param(
  [string]$Container = 'resume-dev-pg',
  [string]$Database = 'resume',
  [string]$User = 'postgres'
)
$ErrorActionPreference = 'Stop'
$sql = Join-Path $PSScriptRoot 'dev-seed.sql'
if (-not (Test-Path $sql)) { throw "Missing $sql" }

# Stream the file through stdin as raw UTF-8 bytes so Hebrew text survives the Windows console code page.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'docker'
$psi.Arguments = "exec -i $Container psql -U $User -d $Database -v ON_ERROR_STOP=1 -q"
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$p = [System.Diagnostics.Process]::Start($psi)
$bytes = [System.IO.File]::ReadAllBytes($sql)
$p.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
$p.StandardInput.Close()
$out = $p.StandardOutput.ReadToEnd()
$err = $p.StandardError.ReadToEnd()
$p.WaitForExit()
if ($p.ExitCode -ne 0) { Write-Error $err; exit $p.ExitCode }
Write-Host $out
Write-Host 'Seed complete. Check: curl http://localhost:4200/api/profiles'
