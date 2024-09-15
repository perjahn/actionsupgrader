using System.Text.Json.Serialization;

public class GithubRepository
{
    public string name { get; set; } = string.Empty;
    public string clone_url { get; set; } = string.Empty;
    //public string default_branch { get; set; } = string.Empty;
    public string role_name { get; set; } = string.Empty;
    public bool archived { get; set; } = false;
}

public class GithubTag
{
    public string name { get; set; } = string.Empty;
}

public class GithubPR
{
    public int number { get; set; } = 0;
    public string title { get; set; } = string.Empty;
    public string body { get; set; } = string.Empty;
    public GithubPRHead head { get; set; } = new();
    [JsonPropertyName("base")]
    public GithubPRBase basex { get; set; } = new();
}

public class GithubPRHead
{
    [JsonPropertyName("ref")]
    public string refx { get; set; } = string.Empty;
}

public class GithubPRBase
{
    [JsonPropertyName("ref")]
    public string refx { get; set; } = string.Empty;
}

public class GithubPRPayload
{
    public string title { get; set; } = string.Empty;
    public string body { get; set; } = string.Empty;
    public string head { get; set; } = string.Empty;
    [JsonPropertyName("base")]
    public string basex { get; set; } = string.Empty;
}

public class ConfigLogLevel
{
    public string Default { get; set; } = string.Empty;
    [JsonPropertyName("Microsoft.AspNetCore")]
    public string MicrosoftAspNetCore { get; set; } = string.Empty;
}

public class ConfigLogging
{
    public ConfigLogLevel LogLevel { get; set; } = new();
}

public class ConfigFile
{
    public ConfigLogging Logging { get; set; } = new();
    public string AllowedHosts { get; set; } = string.Empty;
    public string githuborgname { get; set; } = string.Empty;
    public string githubtoken { get; set; } = string.Empty;
    public string gituseremail { get; set; } = string.Empty;
    public string gitusername { get; set; } = string.Empty;
}
