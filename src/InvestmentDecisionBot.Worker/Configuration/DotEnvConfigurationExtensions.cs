using Microsoft.Extensions.Configuration;

namespace InvestmentDecisionBot.Worker.Configuration;

public static class DotEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddDotEnvFiles(this IConfigurationBuilder configuration, params string[] startDirectories)
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in FindDotEnvFiles(startDirectories))
        {
            if (!loaded.Add(path))
            {
                continue;
            }

            var values = ParseFile(path);
            if (values.Count > 0)
            {
                configuration.AddInMemoryCollection(values);
            }
        }

        return configuration;
    }

    private static IEnumerable<string> FindDotEnvFiles(IEnumerable<string> startDirectories)
    {
        var directories = startDirectories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var startDirectory in directories)
        {
            var current = new DirectoryInfo(startDirectory);
            while (current is not null)
            {
                var envPath = Path.Combine(current.FullName, ".env");
                if (File.Exists(envPath))
                {
                    yield return envPath;
                }

                var localPath = Path.Combine(current.FullName, ".env.local");
                if (File.Exists(localPath))
                {
                    yield return localPath;
                }

                current = current.Parent;
            }
        }
    }

    private static Dictionary<string, string?> ParseFile(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = StripInlineComment(line[(separator + 1)..].Trim());
            values[key] = Unquote(value);
        }

        return values;
    }

    private static string StripInlineComment(string value)
    {
        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
            }
            else if (value[i] == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
            }
            else if (value[i] == '#' && !inSingleQuotes && !inDoubleQuotes && (i == 0 || char.IsWhiteSpace(value[i - 1])))
            {
                return value[..i].TrimEnd();
            }
        }

        return value;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1]
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];
        }

        return value;
    }
}
