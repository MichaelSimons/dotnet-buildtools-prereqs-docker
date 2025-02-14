using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.DotNet.BuildToolsPrereqs.Docker.Tests;

public class CodeOwnersTests
{
    private readonly Dictionary<string, string> codeOwnerEntries = new Dictionary<string, string>();

    public CodeOwnersTests()
    {
        ReadCodeOwnersFile();
    }

    private void ReadCodeOwnersFile()
    {
        string codeOwnersFilePath = Path.Combine(Config.RepoRoot, "CODEOWNERS");
        var lines = File.ReadAllLines(codeOwnersFilePath).ToList();

        // Ensure the last line is read by adding a newline if missing
        if (!string.IsNullOrEmpty(lines.Last()))
        {
            lines.Add("");
        }

        foreach (var line in lines)
        {
            // Skip blank lines, comments, and * paths
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("*"))
            {
                continue;
            }

            var parts = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var path = parts[0].Trim();
            var owners = parts[1].Trim();

            // Escape periods
            path = Regex.Escape(path);

            // A single asterisk matches anything that is not a slash (as long as it is not at the beginning of a pattern)
            if (!path.StartsWith("*"))
            {
                path = Regex.Replace(path, @"([^*]|^)\*([^*]|$)", "$1[^/]*$2");
            }

            // Trailing /** and leading **/ should match anything in all directories
            path = path.Replace("/**", "/.*").Replace("**/", ".*/");

            // If the asterisk is at the beginning of the pattern or the pattern does not start with a slash, then match everything
            if (path.StartsWith("*"))
            {
                path = $".{path}";
            }
            else if (!path.StartsWith("/") && !path.StartsWith(".*"))
            {
                path = $".*{path}";
            }

            // If there is a trailing slash, then match everything below the directory
            if (path.EndsWith("/"))
            {
                path = $"{path}.*";
            }

            path = $"^{path}$";

            codeOwnerEntries[path] = owners;
        }
    }

    [Fact]
    public void OwnersAreTeams()
    {
        var nonTeamOwners = new List<string>();

        foreach (var owners in codeOwnerEntries.Values)
        {
            var codeOwners = owners.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var owner in codeOwners)
            {
                if (!owner.Contains("/"))
                {
                    nonTeamOwners.Add(owner);
                }
            }
        }

        Assert.Empty(nonTeamOwners);
    }

    [Fact]
    public void OwnersIncludeDockerReviewers()
    {
        const string dotnetDockerReviewersTeam = "@dotnet/dotnet-docker-reviewers";

        foreach (var owners in codeOwnerEntries.Values)
        {
            Assert.Contains(dotnetDockerReviewersTeam, owners.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    [Fact]
    public void PathsAreUsed()
    {
        var allFiles = Directory.GetFiles(Config.RepoRoot, "*", SearchOption.AllDirectories)
                                .Select(f => f.Replace("\\", "/").Substring(1))
                                .ToList();
        var unusedPaths = new List<string>();

        foreach (var path in codeOwnerEntries.Keys)
        {
            var pathUsed = allFiles.Any(file => Regex.IsMatch(file, path));
            if (!pathUsed)
            {
                var originalPath = path.Replace("[^/]*", "*").Trim('^', '$');
                unusedPaths.Add(originalPath);
            }
        }

        Assert.Empty(unusedPaths);
    }

    [Fact]
    public void DockerfilesHaveOwners()
    {
        var dockerfiles = Directory.GetFiles(Config.RepoRoot, "Dockerfile", SearchOption.AllDirectories)
                                   .Select(f => f.Replace("\\", "/").Substring(1))
                                   .ToList();
        var filesWithoutOwner = new List<string>();

        foreach (var file in dockerfiles)
        {
            var ownerFound = codeOwnerEntries.Keys.Any(pattern => Regex.IsMatch(file, pattern));
            if (!ownerFound)
            {
                filesWithoutOwner.Add(file);
            }
        }

        Assert.Empty(filesWithoutOwner);
    }
}
