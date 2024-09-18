
using System;
using System.Collections.Generic;
using System.Linq;

class StatItem
{
    public List<string> Repos { get; set; } = [];
    public List<(string from, string to)> VersionPairs { get; set; } = [];
    public int StepCount { get; set; }
}

class Statistics
{
    static readonly string[] StatisticsHeaders = ["Action", "Repos", "Steps"];

    public static void ShowStatistics(List<UpdateStep> stepsToUpdate)
    {
        Dictionary<string, StatItem> statistics = [];

        foreach (var step in stepsToUpdate)
        {
            if (statistics.TryGetValue(step.OwnerRepo, out var statItem))
            {
                if (!statItem.Repos.Contains(step.RepoName))
                {
                    statItem.Repos.Add(step.RepoName);
                }
                if (!statItem.VersionPairs.Any(p => p.to == step.OldVersion && p.from == step.Version))
                {
                    statItem.VersionPairs.Add((step.OldVersion, step.Version));
                }
                statItem.StepCount++;
            }
            else
            {
                List<string> repos = [];
                repos.Add(step.RepoName);
                List<(string from, string to)> versionPairs = [];
                versionPairs.Add((step.OldVersion, step.Version));
                statistics[step.OwnerRepo] = new StatItem { Repos = repos, VersionPairs = versionPairs, StepCount = 1 };
            }
        }

        var table = new List<string[]> { StatisticsHeaders };
        foreach (var steps in statistics.OrderBy(s => s.Value.Repos.Count).ThenBy(s => s.Value.StepCount).ThenBy(s => s.Key))
        {
            var updates = string.Join(",", steps.Value.VersionPairs.Select(p => $"{p.from}").Distinct().OrderBy(p => p)) + $" -> {steps.Value.VersionPairs.First().to}";
            table.Add([$"{steps.Key} ({updates})", steps.Value.Repos.Count.ToString(), steps.Value.StepCount.ToString()]);
        }

        var columnWidths = new int[table[0].Length];
        for (var i = 0; i < table[0].Length; i++)
        {
            columnWidths[i] = table.Max(r => r[i].Length);
        }

        foreach (var row in table)
        {
            for (var col = 0; col < row.Length; col++)
            {
                if (col == 0)
                {
                    Console.Write(row[col].PadRight(columnWidths[col] + 1));
                }
                else
                {
                    Console.Write(row[col].PadLeft(columnWidths[col] + 1));
                }
            }
            Console.WriteLine();
        }

        Console.Write("Total".PadRight(columnWidths[0] + 1));
        Console.Write(stepsToUpdate.GroupBy(s => s.RepoName).Count().ToString().PadLeft(columnWidths[1] + 1));
        Console.WriteLine(statistics.Sum(s => s.Value.StepCount).ToString().PadLeft(columnWidths[2] + 1));
    }
}
