using System;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var parsedArgs = args.ToList();

        var dontClone = ArgumentParser.Flag(parsedArgs, "-c");
        var dryRun = ArgumentParser.Flag(parsedArgs, "-d");
        var excludeRepos = ArgumentParser.String(parsedArgs, "-e", string.Empty);
        var folder = ArgumentParser.String(parsedArgs, "-f", "/tmp/actionsupgrader_repos");
        var splitPRs = ArgumentParser.Flag(parsedArgs, "-s");
        var teams = ArgumentParser.String(parsedArgs, "-t", string.Empty);
        var isUser = ArgumentParser.Flag(parsedArgs, "-u");
        var entity = (isUser ? "users/" : "orgs/") + Config.GithubOrgName;

        if (parsedArgs.Count != 0 ||
            Config.GithubOrgName == string.Empty ||
            !Config.GithubToken.StartsWith("ghp_") || Config.GithubToken.Length != 40 ||
            Config.GitUserName == string.Empty ||
            Config.GitUserEmail == string.Empty)
        {
            var usage =
                "Usage: actionsupgrader [-c] [-d] [-e repo] [-f folder] [-s] [-t team] [-u]\n" +
                "\n" +
                "-c:   Don't clone any repos, assume they are already in the local file system.\n" +
                "-d:   Dry-run, don't push/skip PR creation and only update the local repos.\n" +
                "-e:   Comma separated list of repo names to exclude.\n" +
                "-f:   Scratch folder, default is '/tmp/actionsupgrader_repos'\n" +
                "-s:   Split PRs, create one PR per action.\n" +
                "-t:   Only update repos for particular team. Comma separated list of team names.\n" +
                "-u:   Github user instead of organization.\n" +
                "\n" +
                "Mandatory environment variables:\n" +
                "GITHUB_ORGNAME:  Github organization name (or user name, with -u).\n" +
                "GITHUB_TOKEN:    Must be set to a valid Github token, i.e. ghp_...\n" +
                "GIT_USEREMAIL:   Git commit user email.\n" +
                "GIT_USERNAME:    Git commit user name.\n";
            Console.WriteLine(usage);

            return 1;
        }

        var success = await Actions.UpdateActions(entity, folder,
            excludeRepos.Split(',', StringSplitOptions.RemoveEmptyEntries),
            teams.Split(',', StringSplitOptions.RemoveEmptyEntries),
            dontClone, splitPRs, dryRun);

        return success ? 0 : 1;
    }
}
