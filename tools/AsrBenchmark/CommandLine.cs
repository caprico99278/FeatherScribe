namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>Minimal "--key value" option parser.</summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine(string command)
    {
        Command = command;
    }

    public string Command { get; }

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("No command given.");
        }

        var commandLine = new CommandLine(args[0].ToLowerInvariant());
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--option value' but got '{key}'.");
            }

            commandLine._options[key[2..]] = args[++i];
        }

        return commandLine;
    }

    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public string Require(string name)
        => Get(name) ?? throw new ArgumentException($"--{name} is required.");

    public int GetInt(string name, int defaultValue)
        => Get(name) is { } value ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : defaultValue;

    /// <summary>
    /// Resolves an option path: explicit values are relative to the current directory;
    /// defaults are relative to the repository root (the folder holding FeatherScribe.slnx).
    /// </summary>
    public string GetPath(string name, string repoRelativeDefault)
        => Get(name) is { } value
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(RepoRoot(), repoRelativeDefault));

    public static string RepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Environment.CurrentDirectory;
    }
}
