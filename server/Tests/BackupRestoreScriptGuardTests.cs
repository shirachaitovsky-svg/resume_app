using System.Diagnostics;
using System.Text;

namespace ResumeApp.Tests;

/// <summary>A fact that is skipped (with the reason shown) unless Windows PowerShell is available.</summary>
public sealed class PowerShellFactAttribute : FactAttribute
{
    public PowerShellFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "scripts/backup-restore.ps1 is tested on Windows only."; return; }
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -Command exit 0") { CreateNoWindow = true, UseShellExecute = false });
            probe!.WaitForExit();
        }
        catch (Exception)
        {
            Skip = "powershell is not available on this machine.";
        }
    }
}

/// <summary>
/// Story 4.3: runs the real scripts/backup-restore.ps1 for the refusals that happen before any docker call.
/// The container name points at nothing, so reaching docker would produce a different message; nothing is touched.
/// </summary>
public class BackupRestoreScriptGuardTests
{
    private static string ScriptPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "backup-restore.ps1");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("scripts/backup-restore.ps1 was not found above " + AppContext.BaseDirectory);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] scriptArgs)
    {
        var psi = new ProcessStartInfo("powershell")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-NoProfile", "-File", ScriptPath(), "restore" }.Concat(scriptArgs)) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-Container");
        psi.ArgumentList.Add("no-such-container-for-guard-tests");
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
    }

    private async Task AssertRefusedAsync(string expectedMessage, params string[] scriptArgs)
    {
        var (exit, _, stderr) = await RunAsync(scriptArgs);
        Assert.NotEqual(0, exit);
        Assert.Contains(expectedMessage, stderr);
        Assert.DoesNotContain("הקונטיינר", stderr); // docker was never reached
    }

    [PowerShellFact]
    public Task Target_equal_to_the_source_is_refused() => AssertRefusedAsync("זהה למסד המקור", ScriptPath(), "-SourceDatabase", "resume_restore_x", "-TargetDatabase", "resume_restore_x");

    [PowerShellFact]
    public Task Target_resume_is_refused() => AssertRefusedAsync("מוגן", ScriptPath(), "-SourceDatabase", "other_source", "-TargetDatabase", "resume");

    [PowerShellFact]
    public Task Target_resume_e2e_is_refused_even_with_force() => AssertRefusedAsync("מוגן", ScriptPath(), "-SourceDatabase", "other_source", "-TargetDatabase", "resume_e2e", "-Force");

    [PowerShellFact]
    public Task Invalid_database_name_is_refused() => AssertRefusedAsync("שם מסד לא תקין", ScriptPath(), "-TargetDatabase", "Bad-Name;drop");

    [PowerShellFact]
    public Task Target_outside_resume_restore_needs_force() => AssertRefusedAsync("resume_restore", ScriptPath(), "-SourceDatabase", "other_source", "-TargetDatabase", "some_other_db");

    [PowerShellFact]
    public Task Missing_dump_file_is_refused() => AssertRefusedAsync("לא נמצא", Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".dump"));
}
