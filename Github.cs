using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class Github
{
    static Uri BaseAdress { get; set; } = new("https://api.github.com");
    static string CacheFolder { get; set; } = "cache";
    static AuthenticationHeaderValue AuthHeader { get; set; } = new("Bearer", Config.GithubToken);
    static ProductInfoHeaderValue UserAgent { get; set; } = new("useragent", "1.0");
    static int PerPage { get; set; } = 100;
    static JsonSerializerOptions JsonOptions { get; set; } = new() { WriteIndented = true };
    static readonly ILogger Logger = LoggerFactory.Create(b => b.AddSimpleConsole(options => { options.TimestampFormat = "HH:mm:ss "; })).CreateLogger<Github>();

    public static async Task<List<GithubRepository>> GetAllRepos(string entity)
    {
        using HttpClient client = new() { BaseAddress = BaseAdress };
        client.DefaultRequestHeaders.Authorization = AuthHeader;
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        List<GithubRepository> githubRepositories = [];

        var filename = "repos.txt";
        if (File.Exists(filename))
        {
            Logger.LogInformation("Using repo list from: '{Filename}'", filename);
            var reponames = File.ReadAllLines(filename);
            var owner = entity.Split('/')[1];

            foreach (var reponame in reponames)
            {
                var repoAddress = $"repos/{owner}/{reponame}";

                Logger.LogInformation("Getting repo: '{RepoAddress}'", repoAddress);
                using var response = await client.GetAsync(repoAddress);

                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("Warning: Ignoring repo '{Reponame}'. {StatusCode} ({ReasonPhrase}): '{Json}'", reponame, response.StatusCode, response.ReasonPhrase, json);
                    continue;
                }

                var repo = JsonSerializer.Deserialize<GithubRepository>(json);
                if (repo == null)
                {
                    Logger.LogInformation("Warning: Ignoring repo '{Reponame}'. Couldn't deserialize json: '{Json}'", reponame, json);
                    continue;
                }

                githubRepositories.Add(repo);
            }
        }
        else
        {
            var address = $"{entity}/repos?per_page={PerPage}";
            while (address != string.Empty)
            {
                Logger.LogInformation("Getting repos: '{Address}'", address);
                using var response = await client.GetAsync(address);
                address = GetNextLink(response.Headers);

                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("Warning: Ignoring repos. {StatusCode} ({ReasonPhrase}): '{Json}'", response.StatusCode, response.ReasonPhrase, json);
                    foreach (var header in response.Headers)
                    {
                        Logger.LogInformation("{HeaderKey}: '{HeaderValue}'", header.Key, string.Join("', '", header.Value));
                    }
                    continue;
                }

                var repos = JsonSerializer.Deserialize<GithubRepository[]>(json);
                if (repos == null)
                {
                    Logger.LogInformation("Warning: Ignoring repos. Couldn't deserialize json: '{Json}'", json);
                    continue;
                }

                githubRepositories.AddRange(repos);
            }
        }

        foreach (var repo in githubRepositories)
        {
            if (repo.clone_url.EndsWith(".git"))
            {
                repo.clone_url = repo.clone_url[..^4];
            }
        }

        return githubRepositories;
    }

    public static async Task<List<GithubRepository>> GetAllTeamRepositories(string entity, string[] teamnames)
    {
        using HttpClient client = new() { BaseAddress = BaseAdress };
        client.DefaultRequestHeaders.Authorization = AuthHeader;
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        var allteamrepos = (await Task.WhenAll(teamnames.Select(t => GetTeamRepositories(client, entity, t))))
           .SelectMany(t => t)
           .ToList();

        foreach (var repo in allteamrepos)
        {
            if (repo.clone_url.EndsWith(".git"))
            {
                repo.clone_url = repo.clone_url[..^4];
            }
        }

        return allteamrepos;
    }

    static async Task<List<GithubRepository>> GetTeamRepositories(HttpClient client, string entity, string teamname)
    {
        var shortFilename = $"TeamRepositories_{CleanFileName(teamname)}";
        var ext = $"_{DateTime.Today:yyyyMMdd}.json";

        List<GithubRepository>? jsonarrayCached;
        if ((jsonarrayCached = LoadCachedTeamRepositories(shortFilename, ext)) != null)
        {
            return jsonarrayCached;
        }

        List<GithubRepository> teamrepos = [];
        List<string> allcontent = [];
        var address = $"{entity}/teams/{teamname}/repos?per_page={PerPage}";
        while (address != string.Empty)
        {
            Logger.LogInformation("Getting Team repos: '{Teamname}' '{Address}'", teamname, address);

            GithubRepository[] jsonarray;
            var content = string.Empty;
            try
            {
                var response = await client.GetAsync(address);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Get '{Address}', StatusCode: {StatusCode}", address, response.StatusCode);
                }
                content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Result: >>>{Content}<<<", content);
                }
                address = GetNextLink(response.Headers);

                jsonarray = JsonSerializer.Deserialize<GithubRepository[]>(content) ?? [];
                allcontent.Add(content);
            }
            catch (Exception ex)
            {
                Logger.LogError("Get '{Address}', Result: >>>{Content}<<<, Exception: >>>{Exception}<<<", address, content, ex);
                continue;
            }

            teamrepos.AddRange(jsonarray);
        }

        Logger.LogInformation("Got Team repos: {TeamRepoCount}", teamrepos.Count);

        SaveCachedJsonArray(shortFilename, allcontent, "Team repositories", ext);

        return teamrepos;
    }

    public static async Task<List<(string ownerRepo, string tag)>> GetRepoTags(string[] ownerRepos)
    {
        var filename = "tags.txt";
        if (File.Exists(filename))
        {
            Logger.LogInformation("Using cached tags from: '{Filename}'", filename);
            var cachedTags = File.ReadAllLines(filename);
            return cachedTags.Select(t => (t.Split(' ')[0], t.Split(' ')[1])).ToList();
        }

        using HttpClient client = new() { BaseAddress = BaseAdress };
        client.DefaultRequestHeaders.Authorization = AuthHeader;
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        List<(string ownerRepo, string tag)> tags = [];

        foreach (var ownerRepo in ownerRepos)
        {
            var address = $"repos/{ownerRepo}/tags?per_page={PerPage}";
            while (address != string.Empty)
            {
                Logger.LogInformation("Getting Tags: '{Address}'", address);

                var content = string.Empty;
                GithubTag[] jsonarray = [];
                try
                {
                    var response = await client.GetAsync(address);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.LogError("Get '{Address}', StatusCode: {StatusCode}", address, response.StatusCode);
                    }
                    content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.LogError("Result: >>>{Content}<<<", content);
                    }
                    address = GetNextLink(response.Headers);

                    jsonarray = JsonSerializer.Deserialize<GithubTag[]>(content) ?? [];
                }
                catch (Exception ex)
                {
                    Logger.LogError("Get '{Address}', Result: >>>{Content}<<<, Exception: >>>{Exception}<<<", address, content, ex);
                    continue;
                }

                tags.AddRange(jsonarray.Select(tag => (ownerRepo, tag: tag.name)));
            }
        }

        Logger.LogInformation("Got Tags: {TagCount}", tags.Count);

        return tags;
    }

    public static async Task<List<GithubPR>> GetPRs(string owner, string repo)
    {
        using HttpClient client = new() { BaseAddress = BaseAdress };
        client.DefaultRequestHeaders.Authorization = AuthHeader;
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        List<GithubPR> prs = [];

        var address = $"repos/{owner}/{repo}/pulls?per_page={PerPage}";
        while (address != string.Empty)
        {
            Logger.LogInformation("Getting PRs: '{Address}'", address);

            var content = string.Empty;
            GithubPR[] jsonarray;
            try
            {
                var response = await client.GetAsync(address);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Get '{Address}', StatusCode: {StatusCode}", address, response.StatusCode);
                }
                content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Result: >>>{Content}<<<", content);
                }
                address = GetNextLink(response.Headers);

                jsonarray = JsonSerializer.Deserialize<GithubPR[]>(content) ?? [];
            }
            catch (Exception ex)
            {
                Logger.LogError("Get '{Address}', Result: >>>{Content}<<<, Exception: >>>{Exception}<<<", address, content, ex);
                continue;
            }

            prs.AddRange(jsonarray);
        }

        Logger.LogInformation("Got PRs: {PRCount}", prs.Count);

        return prs;
    }

    public static async Task CreatePR(string owner, string repo, string title, string message, string branchFrom, string branchTo, bool dryRun)
    {
        using HttpClient client = new() { BaseAddress = BaseAdress };
        client.DefaultRequestHeaders.Authorization = AuthHeader;
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        var address = $"repos/{owner}/{repo}/pulls";
        Logger.LogInformation("Creating PR: '{Address}'", address);

        GithubPRPayload prpayload = new()
        {
            title = title,
            body = message,
            head = branchFrom,
            basex = branchTo
        };
        using StringContent stringContent = new(JsonSerializer.Serialize(prpayload));

        try
        {
            if (!dryRun)
            {
                var response = await client.PostAsync(address, stringContent);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Post '{Address}', StatusCode: {StatusCode}", address, response.StatusCode);
                }
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Result: >>>{Content}<<<", content);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Post '{Address}', Payload: >>>{Content}<<<, Exception: >>>{Exception}<<<", address, stringContent, ex);
        }

        Logger.LogInformation("Created PR.");
    }

    public static async Task UpdatePR(string owner, string repo, int number, string title, string message, string branchFrom, string branchTo, bool dryRun)
    {
        using HttpClient client = new() { BaseAddress = BaseAdress };
        client.DefaultRequestHeaders.Authorization = AuthHeader;
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        var address = $"repos/{owner}/{repo}/pulls/{number}";
        Logger.LogInformation("Updating PR: '{Address}'", address);

        GithubPRPayload prpayload = new()
        {
            title = title,
            body = message,
            head = branchFrom,
            basex = branchTo
        };
        using StringContent stringContent = new(JsonSerializer.Serialize(prpayload));

        try
        {
            if (!dryRun)
            {
                var response = await client.PatchAsync(address, stringContent);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Patch '{Address}', StatusCode: {StatusCode}", address, response.StatusCode);
                }
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Result: >>>{Content}<<<", content);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Patch '{Address}', Payload: >>>{Content}<<<, Exception: >>>{Exception}<<<", address, stringContent, ex);
        }

        Logger.LogInformation("Updated PR.");
    }

    static List<GithubRepository>? LoadCachedTeamRepositories(string shortFilename, string ext)
    {
        List<GithubRepository> teamrepos = [];

        var filename = Path.Combine(CacheFolder, $"{shortFilename}{ext}");
        if (!File.Exists(filename))
        {
            return null;
        }

        Logger.LogInformation("Using cached team repositories from: '{Filename}'", filename);
        return JsonSerializer.Deserialize<List<GithubRepository>>(File.ReadAllText(filename)) ?? teamrepos;
    }

    static void SaveCachedJsonArray(string shortFilename, List<string> jsonstrings, string typename, string ext)
    {
        if (!Directory.Exists(CacheFolder))
        {
            Logger.LogInformation("Creating folder: '{CacheFolder}'", CacheFolder);
            Directory.CreateDirectory(CacheFolder);
        }

        var filename = Path.Combine(CacheFolder, $"{shortFilename}{ext}");

        if (jsonstrings.Count == 0)
        {
            Logger.LogInformation("No {Typename} to save to: '{Filename}'", typename, filename);
            return;
        }

        var oldfiles = Directory.GetFiles(CacheFolder);
        foreach (var oldfile in oldfiles.Where(f => Path.GetFileName(f).StartsWith(shortFilename) && !f.EndsWith(ext)))
        {
            Logger.LogInformation("Deleting old file: '{OldFile}'", oldfile);
            File.Delete(oldfile);
        }

        var alljson = "[" + string.Join(",", jsonstrings.Select(j =>
        {
            var json = j.Trim();
            return json.StartsWith('[') && json.EndsWith(']') ? json[1..^1] : json;
        }).Where(j => j.Trim() != string.Empty)) + "]";

        var jsonelement = JsonSerializer.Deserialize<JsonElement>(alljson);
        var pretty = JsonSerializer.Serialize(jsonelement, JsonOptions);

        Logger.LogInformation("Saving {Typename} to: '{Filename}'", typename, filename);
        File.WriteAllText(filename, pretty);
    }

    // link: <https://api.github.com/repositories/1300192/issues?page=4>; rel="next", ...
    static string GetNextLink(HttpResponseHeaders headers)
    {
        if (headers.Contains("Link"))
        {
            var links = headers.GetValues("Link").SelectMany(l => l.Split(',')).ToArray();
            foreach (var link in links)
            {
                var parts = link.Split(';');
                if (parts.Length == 2 && parts[0].Trim().StartsWith('<') && parts[0].Trim().EndsWith('>') && parts[1].Trim() == "rel=\"next\"")
                {
                    var url = parts[0].Trim()[1..^1];
                    return url;
                }
            }
        }

        return string.Empty;
    }

    static string CleanFileName(string filename)
    {
        StringBuilder result = new();
        foreach (var c in filename)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')
            {
                result.Append(c);
            }
            else
            {
                result.Append($"%{c:X}");
            }
        }
        return result.Length > 50 ? result.ToString()[..50] : result.ToString();
    }
}
