using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public class Config
{
    static ConfigFile ConfigFile
    {
        get
        {
            var filenames = new[] {
                "appsettings.Development.json", "appsettings.json",
                "src/appsettings.Development.json", "src/appsettings.json",
                "../src/appsettings.Development.json", "../src/appsettings.json" };
            var filename = filenames.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException($"Could not find any appsettings.json or appsettings.Development.json file.");

            var content = File.ReadAllText(filename);
            var jsonobject = JsonSerializer.Deserialize<ConfigFile>(content) ?? new ConfigFile();
            return jsonobject;
        }
    }

    public static string GithubOrgName
    {
        get
        {
            var envvalue = Environment.GetEnvironmentVariable("GITHUB_ORGNAME");
            return !string.IsNullOrWhiteSpace(envvalue) ? envvalue : ConfigFile.githuborgname;
        }
    }

    public static string GithubToken
    {
        get
        {
            var envvalue = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            return !string.IsNullOrWhiteSpace(envvalue) ? envvalue : ConfigFile.githubtoken;
        }
    }

    public static string GitUserEmail
    {
        get
        {
            var envvalue = Environment.GetEnvironmentVariable("GIT_USEREMAIL");
            return !string.IsNullOrWhiteSpace(envvalue) ? envvalue : ConfigFile.gituseremail;
        }
    }

    public static string GitUserName
    {
        get
        {
            var envvalue = Environment.GetEnvironmentVariable("GIT_USERNAME");
            return !string.IsNullOrWhiteSpace(envvalue) ? envvalue : ConfigFile.gitusername;
        }
    }
}
