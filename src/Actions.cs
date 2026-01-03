using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

class UpdateStep
{
    public string RepoName { get; set; } = string.Empty;
    public string WorkflowFile { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public string OwnerRepo { get; set; } = string.Empty;
    public string OldVersion { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Pushed { get; set; }
}

class Actions
{
    static readonly ILogger Logger = LoggerFactory.Create(b => b.AddSimpleConsole(options => { options.TimestampFormat = "HH:mm:ss "; })).CreateLogger<Actions>();

    public static int GetDefaultParallelism()
    {
        // This scaling is based on empirical experience and need to work for both build agents and local machines.
        // Some cases:
        // * Linux build agents in k8s on VMs with 4-8 vcpus.
        // * Laptops with 8-16 cores (8-16 cpu threads) when using win/mac.
        // * Docker in linux on 64 cores (128 cpu threads).
        // Adjust the default scaling if desired, but motivate why in this comment.

        var cores = Environment.ProcessorCount;
        if (cores >= 16)
        {
            return cores / 2;
        }
        else if (cores >= 8)
        {
            return 8;
        }

        return cores;
    }

    public static async Task<bool> UpdateActions(string entity, string folder, string[] excludeRepos, string[] teams, int maxsizekb, bool dontClone, bool splitPRs, bool dryRun, bool noforks, bool approve)
    {
        var now = DateTime.Now;

        if (!dontClone)
        {
            var cloneresult = await CloneRepos(entity, folder, excludeRepos, teams, maxsizekb, noforks);
            if (!cloneresult)
            {
                return false;
            }
        }

        var steps = await GetStepsToUpdate(folder);

        var success = await CreatePRs(entity, steps, splitPRs, now, dryRun, approve);

        Statistics.ShowStatistics(steps);

        return success;
    }

    static async Task<bool> CreatePRs(string entity, List<UpdateStep> steps, bool splitPRs, DateTime now, bool dryRun, bool approve)
    {
        if (splitPRs)
        {
            throw new NotImplementedException("Non-combined PRs are not yet supported.");
        }

        IGrouping<string, UpdateStep>[] allSteps = [.. steps.GroupBy(s => s.RepoName).OrderBy(s => s.Key)];

        var success = true;

        List<(int? updateNumber, string? updateTitle, string repoFolder, string remoteBranch, string owner, string repoName, string title, string message, string defaultBranch)> pushPRs = [];

        foreach (var repoSteps in allSteps)
        {
            IGrouping<string, UpdateStep>[] workflowFileSteps = [.. repoSteps.GroupBy(s => s.WorkflowFile).OrderBy(s => s.Key)];

            foreach (var workflowFile in workflowFileSteps)
            {
                if (!UpdateWorkflowFile(workflowFile.Key, [.. workflowFile]))
                {
                    success = false;
                }
            }

            var repoName = repoSteps.Key;
            var title = $"Updated {repoSteps.Count()} github actions.";
            var message = "Hello dear maintainer!\n\n" +
                          "I have updated the following github actions:\n\n" +
                          string.Join("\n", repoSteps.OrderBy(s => CleanWorkflowName(s.WorkflowName)).ThenBy(s => s.StepName).Select(s => $"* {CleanWorkflowName(s.WorkflowName)}: {s.StepName} ({s.OldVersion} -> {s.Version})")) +
                          "\n\n" +
                          "Please review and merge this PR.\n\n" +
                          "Thanks!";
            var remoteBranch = $"actionsupgrader-{now:yyyyMMdd}";

            var repoFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(repoSteps.First().WorkflowFile))) ?? string.Empty;

            Logger.LogInformation("Repo: '{RepoFolder}'", repoFolder);

            var defaultBranch = Git.GetCurrentBranch(repoFolder);
            Logger.LogInformation("Git default branch: '{DefaultBranch}'", defaultBranch);

            var owner = entity.Split('/')[1];

            var repoPRs = await Github.GetPRs(owner, repoName);
            foreach (var pr in repoPRs)
            {
                Logger.LogInformation("PR: Title: '{Title}', Head (from): '{From}', Base (to): '{To}'", pr.title, pr.head.refx, pr.basex.refx);
            }

            var foundIdenticalPR = repoPRs.FirstOrDefault(pr => pr.head.refx.Length == remoteBranch.Length && pr.head.refx.StartsWith("actionsupgrader-") && pr.title == title && pr.body == message);
            if (foundIdenticalPR != null)
            {
                Logger.LogInformation("Not updating existing PR: '{Title}'", foundIdenticalPR.title);
                continue;
            }
            foreach (var step in repoSteps)
            {
                step.Pushed = true;
            }

            Logger.LogInformation("Git user name: '{GitUserName}'", Config.GitUserName);
            Git.SetConfig(repoFolder, "user.name", Config.GitUserName);

            Logger.LogInformation("Git user email: '{GitUserEmail}'", Config.GitUserEmail);
            Git.SetConfig(repoFolder, "user.email", Config.GitUserEmail);

            Git.Commit(repoFolder, title);

            var foundPR = repoPRs.FirstOrDefault(pr => pr.head.refx.Length == remoteBranch.Length && pr.head.refx.StartsWith("actionsupgrader-"));
            if (foundPR == null)
            {
                pushPRs.Add((null, null, repoFolder, remoteBranch, owner, repoName, title, message, defaultBranch));
            }
            else
            {
                pushPRs.Add((foundPR.number, foundPR.title, repoFolder, remoteBranch, owner, repoName, title, message, defaultBranch));
            }
        }

        if (approve)
        {
            Logger.LogInformation("Creating/updating PRs: {PRCount}", pushPRs.Count);

            foreach (var (updateNumber, updateTitle, repoFolder, remoteBranch, owner, repoName, title, message, defaultBranch) in pushPRs)
            {
                Git.Push(repoFolder, remoteBranch, dryRun);

                if (updateNumber == null)
                {
                    Logger.LogInformation("Creating PR: '{Title}'", title);
                    await Github.CreatePR(owner, repoName, title, message, remoteBranch, defaultBranch, dryRun);
                }
                else
                {
                    Logger.LogInformation("Updating PR: '{Title}'", updateTitle);
                    await Github.UpdatePR(owner, repoName, updateNumber.Value, title, message, remoteBranch, defaultBranch, dryRun);
                }
            }
        }
        else
        {
            Logger.LogInformation("Not creating/updating PRs: {PRCount}", pushPRs.Count);
        }

        return success;
    }

    static string CleanWorkflowName(string workflowName)
    {
        var w = workflowName.Trim();
        if (w.StartsWith('\'') && w.EndsWith('\''))
        {
            w = w[1..^1];
        }
        else if (w.StartsWith('"') && w.EndsWith('"'))
        {
            w = w[1..^1];
        }
        return w;
    }

    static bool UpdateWorkflowFile(string workflowFile, UpdateStep[] steps)
    {
        var rows = File.ReadAllLines(workflowFile);
        var success = true;

        foreach (var step in steps)
        {
            var found = false;

            for (var i = 0; i < rows.Length && !found; i++)
            {
                var trimmedRow = rows[i].Trim();
                string? stepName = null;
                if (trimmedRow.StartsWith("uses: "))
                {
                    stepName = trimmedRow[6..];
                }
                else if (trimmedRow.StartsWith("- uses: "))
                {
                    stepName = trimmedRow[8..];
                }

                if (stepName != null)
                {
                    var indexComment = stepName.IndexOf('#');
                    if (indexComment >= 0)
                    {
                        stepName = stepName[..indexComment];
                    }
                    stepName = stepName.Trim();
                    if (stepName == step.StepName)
                    {
                        var indexStepName = rows[i].IndexOf(step.StepName);
                        var indexVersion = rows[i].IndexOf('@', indexStepName) + 1;
                        var s = rows[i];
                        rows[i] = rows[i][..indexVersion] + step.Version + rows[i][(indexVersion + step.OldVersion.Length)..];
                        Logger.LogInformation("Updated {WorkflowFile}:{Row}: '{OldVersion}' -> '{NewVersion}'", step.WorkflowFile, i, s, rows[i]);
                        found = true;
                    }
                }
            }
            if (!found)
            {
                Logger.LogInformation("Warning: Could not find step: '{StepName}' in workflow: '{WorkflowFile}' in repo: '{RepoName}'", step.StepName, step.WorkflowFile, step.RepoName);
                success = false;
            }
        }

        Logger.LogInformation("Saving: '{WorkflowFile}'", workflowFile);
        File.WriteAllLines(workflowFile, rows);

        return success;
    }

    static async Task<List<UpdateStep>> GetStepsToUpdate(string folder)
    {
        var workflowFiles = GetWorkflowFiles(folder);

        var steps = GetSteps(workflowFiles);
        Logger.LogInformation("Steps: {StepCount}", steps.Count);

        string[] ownerRepos = [.. steps
            .Select(s => s.OwnerRepo)
            .Distinct()];
        Logger.LogInformation("ActionRepos: {ActionRepoCount}", ownerRepos.Length);

        (string ownerRepo, string tag)[] tags = [.. (await Github.GetRepoTags(ownerRepos))
            .Where(t => IsNumericTag(t.tag))
            .OrderBy(t => t.ownerRepo)
            .ThenBy(t => t.tag)];
        Logger.LogInformation("Tags: {TagCount}", tags.Length);

        List<UpdateStep> stepsToUpdate = [];

        foreach (var step in steps.OrderBy(s => s.WorkflowFile).ThenBy(s => s.StepName))
        {
            if (!IsNumericTag(step.OldVersion))
            {
                continue;
            }

            string? highestVersion = null;

            foreach (var tag in tags.Where(t => t.ownerRepo == step.OwnerRepo).Select(t => t.tag))
            {
                if (highestVersion == null)
                {
                    highestVersion = tag;
                }
                else
                {
                    var diff = CompareVersions(tag, highestVersion);
                    if (diff > 0 || (diff == 0 && tag.Length < highestVersion.Length))
                    {
                        highestVersion = tag;
                    }
                }
            }

            if (highestVersion != null && CompareVersions(step.OldVersion, highestVersion) != 0)
            {
                var s = step;
                s.Version = highestVersion;
                stepsToUpdate.Add(s);
            }
        }

        return stepsToUpdate;
    }

    static bool IsNumericTag(string tag)
    {
        for (var i = 0; i < tag.Length; i++)
        {
            if (!((i == 0 && (tag[i] == 'v' || tag[i] == 'V')) || (tag[i] >= '0' && tag[i] <= '9') || tag[i] == '.'))
            {
                return false;
            }
        }

        return true;
    }

    static int CompareVersions(string version1, string version2)
    {
        var versionParts1 = (version1.StartsWith('v') || version1.StartsWith('V') ? version1[1..] : version1).Split('.');
        var versionParts2 = (version2.StartsWith('v') || version2.StartsWith('V') ? version2[1..] : version2).Split('.');

        for (var i = 0; i < Math.Max(versionParts1.Length, versionParts2.Length); i++)
        {
            if (i >= versionParts1.Length)
            {
                return 0;
            }
            if (i >= versionParts2.Length)
            {
                return 0;
            }

            int index;
            index = versionParts1[i].IndexOf('-');
            var v1 = index >= 0 ? versionParts1[i][..index] : versionParts1[i];
            index = versionParts2[i].IndexOf('-');
            var v2 = index >= 0 ? versionParts2[i][..index] : versionParts2[i];

            if (int.TryParse(v1, out var v1i) && int.TryParse(v2, out var v2i))
            {
                if (v1i != v2i)
                {
                    return v1i.CompareTo(v2i);
                }
            }
            else if (v1 != v2)
            {
                return v1.CompareTo(v2);
            }
        }

        return 0;
    }

    static List<(string repoName, string workflowFile)> GetWorkflowFiles(string folder)
    {
        List<(string repoName, string workflowFile)> workflowFiles = [];

        var repofolders = Directory.GetDirectories(folder, "*", SearchOption.TopDirectoryOnly);
        foreach (var repofolder in repofolders)
        {
            if (!Directory.Exists($"{repofolder}/.github/workflows"))
            {
                continue;
            }

            workflowFiles.AddRange(Directory.GetFiles($"{repofolder}/.github/workflows", "*.yml").Concat(
                                   Directory.GetFiles($"{repofolder}/.github/workflows", "*.yaml"))
                                   .Select(f => (Path.GetFileName(repofolder), f)));
        }

        return workflowFiles;
    }

    static List<UpdateStep> GetSteps(List<(string repoName, string workflowFile)> workflowFiles)
    {
        List<UpdateStep> steps = [];

        foreach (var (repoName, workflowFile) in workflowFiles)
        {
            var rows = File.ReadAllLines(workflowFile);

            var workflowName = rows.FirstOrDefault(r => r.StartsWith("name: "));
            if (workflowName != null)
            {
                workflowName = workflowName[6..];

                if (workflowName.StartsWith('\"') && workflowName.EndsWith('\"'))
                {
                    workflowName = workflowName[1..^1];
                }
            }
            else
            {
                workflowName = Path.GetFileNameWithoutExtension(workflowFile);
            }

            foreach (var row in rows)
            {
                var trimmedRow = row.Trim();
                string? stepName = null;
                if (trimmedRow.StartsWith("uses: "))
                {
                    stepName = trimmedRow[6..];
                }
                else if (trimmedRow.StartsWith("- uses: "))
                {
                    stepName = trimmedRow[8..];
                }

                if (stepName != null)
                {
                    var index = stepName.IndexOf('#');
                    if (index >= 0)
                    {
                        stepName = stepName[..index];
                    }
                    stepName = stepName.Trim();
                    if (stepName != string.Empty)
                    {
                        var ownerRepoVersion = GetStepOwnerRepoVersion(stepName);
                        if (ownerRepoVersion != null)
                        {
                            steps.Add(new UpdateStep
                            {
                                RepoName = repoName,
                                WorkflowFile = workflowFile,
                                WorkflowName = workflowName,
                                StepName = stepName,
                                OwnerRepo = ownerRepoVersion.Value.ownerRepo,
                                OldVersion = ownerRepoVersion.Value.version,
                                Version = string.Empty
                            });
                        }
                    }
                }
            }
        }

        return steps;
    }

    static (string ownerRepo, string version)? GetStepOwnerRepoVersion(string stepName)
    {
        var index = stepName.IndexOf('@');
        if (index < 0)
        {
            return null;
        }

        var version = stepName[(index + 1)..];
        if (version == string.Empty)
        {
            return null;
        }

        if ((version.Length == 40 && version.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))) ||
            (version.Length == 71 && version.StartsWith("sha256:") && version[7..].All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))))
        {
            return null;
        }

        var ownerRepo = SubstringBeforeNthIndexOf(stepName[..index], '/', 2);
        return (ownerRepo, version);
    }

    static async Task<bool> CloneRepos(string entity, string folder, string[] excludeRepos, string[] teams, int maxsizekb, bool noforks)
    {
        var parallelism = GetDefaultParallelism();

        if (Directory.Exists(folder))
        {
            Logger.LogInformation("Deleting folder: '{Folder}'", folder);
            Directory.Delete(folder, true);
        }
        _ = Directory.CreateDirectory(folder);

        var repourls = await GetRepoUrls(entity, excludeRepos, teams, maxsizekb, noforks);
        if (repourls == null)
        {
            return false;
        }

        List<(Process process, string repourl)> processes = [];

        var repocount = 0;
        foreach (var repourl in repourls)
        {
            repocount++;

            while (processes.Count(p => { p.process.Refresh(); return !p.process.HasExited; }) >= parallelism)
            {
                await Task.Delay(100);
            }

            var cloneurl = Config.GithubToken != string.Empty ? $"https://{Config.GithubToken}@{repourl[8..]}" : repourl;

            var clonefolder = SubstringAfterNthIndexOf(repourl, '/', 4);
            var index = clonefolder.IndexOf('/');
            if (index >= 0)
            {
                clonefolder = clonefolder[..index];
            }
            clonefolder = Path.Combine(folder, clonefolder);

            Logger.LogInformation("Cloning ({RepoCount}/{RepoCountTotal}): '{RepoUrl}'", repocount, repourls.Length, repourl);

            var process = Process.Start("git", $"clone {cloneurl} {clonefolder}");
            if (process == null)
            {
                Logger.LogInformation("Failed to clone: '{RepoUrl}'", repourl);
            }
            else
            {
                processes.Add((process, repourl));
            }
        }

        var logtimer = 0;
        while (processes.Any(p => { p.process.Refresh(); return !p.process.HasExited; }))
        {
            await Task.Delay(100);

            logtimer++;
            if (logtimer % 100 == 0)
            {
                List<(Process process, string repourl)> stillRunning = [];
                foreach (var process in processes)
                {
                    process.process.Refresh();
                    if (!process.process.HasExited)
                    {
                        stillRunning.Add(process);
                    }
                }

                if (logtimer < 20000)
                {
                    Logger.LogInformation("Still running: {RepoUrls}", stillRunning.Select(p => p.repourl));
                }
                else
                {
                    foreach (var (process, repourl) in stillRunning)
                    {
                        Logger.LogInformation("Killing: '{RepoUrl}'", repourl);
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
        }

        return true;
    }

    static async Task<string[]?> GetRepoUrls(string entity, string[] excludeRepos, string[] teams, int maxsizekb, bool noforks)
    {
        GithubRepository[] repos = [.. teams.Length == 0 ?
            (await Github.GetAllRepos(entity)) :
            (await Github.GetAllTeamRepositories(entity, teams)).Where(r => r.role_name != "read").GroupBy(r => r.clone_url).Select(g => g.First()) ];

        if (repos.Length == 0)
        {
            Logger.LogInformation("Error: No repos found for: '{Entity}'", entity);
            return null;
        }
        var orgCount = repos.Length;
        Logger.LogInformation("Found {RepoCount} repos.", repos.Length);

        var excludeRepos2 = File.Exists("excluderepos.txt") ? File.ReadAllLines("excluderepos.txt") : excludeRepos;

        var archivedCount = 0;
        var toobigCount = 0;
        var forkCount = 0;
        var excludedCount = 0;
        var invalidCount = 0;

        List<string> filteredRepoUrls = [];
        foreach (var repo in repos)
        {
            var ignore = false;

            if (repo.archived)
            {
                archivedCount++;
                ignore = true;
            }
            if (maxsizekb >= 0 && repo.size > maxsizekb)
            {
                toobigCount++;
                ignore = true;
            }
            if (noforks && repo.fork)
            {
                forkCount++;
                ignore = true;
            }
            if (excludeRepos2.Contains(repo.name))
            {
                excludedCount++;
                ignore = true;
            }

            var reponame = SubstringAfterNthIndexOf(repo.clone_url, '/', 4);
            var index = reponame.IndexOf('/');
            if (index >= 0)
            {
                reponame = reponame[..index];
            }
            if (!repo.clone_url.StartsWith("https://") || reponame.Length == 0)
            {
                invalidCount++;
                ignore = true;
                Logger.LogInformation("Ignoring invalid repo url: '{RepoUrl}'", repo.clone_url);
            }

            if (!ignore)
            {
                Logger.LogInformation("Repo name: '{RepoName}'", reponame);
                filteredRepoUrls.Add(repo.clone_url);
            }
        }

        string[] repourls = [.. filteredRepoUrls];
        Array.Sort(repourls);
        Logger.LogInformation("Filtered from {OrgCount} to {RepoCount} repos ({ArchivedCount} archived, {ToobigCount} too big, {ForkCount} forks, {ExcludedCount} excluded, {InvalidCount} invalid).",
            orgCount, repourls.Length, archivedCount, toobigCount, forkCount, excludedCount, invalidCount);

        return repourls;
    }

    static string SubstringAfterNthIndexOf(string text, char find, int number)
    {
        var index = 0;
        for (var i = 0; i < number; i++)
        {
            index = text.IndexOf(find, index + 1);
            if (index < 0)
            {
                return text;
            }
        }

        return text[(index + 1)..];
    }

    static string SubstringBeforeNthIndexOf(string text, char find, int number)
    {
        var index = 0;
        for (var i = 0; i < number; i++)
        {
            index = text.IndexOf(find, index + 1);
            if (index < 0)
            {
                return text;
            }
        }

        return text[..index];
    }
}
