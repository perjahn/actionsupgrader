using System;
using System.Diagnostics;

class Git
{
    public static void SetConfig(string repoFolder, string configName, string value)
    {
        var startInfo = new ProcessStartInfo("git", $"config {configName} \"{value}\"") { WorkingDirectory = repoFolder };
        Process.Start(startInfo)?.WaitForExit();
    }

    public static string GetCurrentBranch(string repoFolder)
    {
        var startInfo = new ProcessStartInfo("git", $"rev-parse --abbrev-ref HEAD") { WorkingDirectory = repoFolder, RedirectStandardOutput = true };
        var process = Process.Start(startInfo) ?? throw new Exception("Failed to start git process.");
        process.WaitForExit();
        var currentBranch = process.StandardOutput.ReadToEnd().Trim();
        var index = currentBranch.LastIndexOf('/');

        return index >= 0 ? currentBranch[(index + 1)..] : currentBranch;
    }

    public static void Commit(string repoFolder, string message)
    {
        // For repos with conflicting folder names with differences in casing, under win/mac, this will usually
        // not commit anything. Though some commands, like "git diff", will magically repair git's index file,
        // and make the commit command work again, but with unexpected results. I.e. please run this tool on a
        // case-sensitive file system, i.e. Linux.
        var startInfo = new ProcessStartInfo("git", $"commit -a -m \"{message}\"") { WorkingDirectory = repoFolder };
        Process.Start(startInfo)?.WaitForExit();
    }

    public static void Push(string repoFolder, string remoteBranch, bool dryRun)
    {
        var startInfo = new ProcessStartInfo("git", $"push origin HEAD:{remoteBranch}") { WorkingDirectory = repoFolder };
        Console.WriteLine($"Pushing to {remoteBranch}: '{startInfo.FileName}' '{startInfo.Arguments}'");
        if (!dryRun)
        {
            Process.Start(startInfo)?.WaitForExit();
        }
    }
}
