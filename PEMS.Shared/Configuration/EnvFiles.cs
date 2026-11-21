namespace PEMS.Shared.Configuration;

/// <summary>
/// Environment settings files (env/&lt;name&gt;.env) and secret files (env/&lt;name&gt;.secrets.env).
/// Pure BCL on purpose: it is used by the web host and by the design-time EF factory, and must not pull in
/// Microsoft.Extensions.Configuration (assembly conflict in this solution).
///
/// File format: one KEY=VALUE per line, '#' starts a comment line, an optional "export " prefix is ignored,
/// a value may be wrapped in single or double quotes, no inline comments and no interpolation (secrets may
/// contain '#' and '$'). Keys use "__" as the section separator, exactly like environment variables
/// (Ollama__Url, Fido2__Origins__0); they are returned with ':' (Ollama:Url).
/// </summary>
public static class EnvFiles
{
    public const string EnvNameVariable = "PEMS_ENV";
    public const string DirectoryVariable = "PEMS_ENV_DIR";
    public const string DirectoryName = "env";

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line[7..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim().Replace("__", ":");
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];

            if (key.Length > 0)
                result[key] = value;
        }
        return result;
    }

    /// <summary>
    /// PEMS_ENV wins; otherwise the host environment maps Development to "local" and UAT to "uat".
    /// Any other host environment returns null: only real environment variables are used.
    /// </summary>
    public static string? ResolveName(string? explicitName, string hostEnvironmentName)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName.Trim();

        return hostEnvironmentName switch
        {
            "Development" => "local",
            "UAT" => "uat",
            _ => null
        };
    }

    /// <summary>PEMS_ENV_DIR if set, otherwise the nearest "env" folder at or above the start directories.</summary>
    public static string? FindDirectory(string? explicitDirectory, params string?[] startDirectories)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            return Directory.Exists(explicitDirectory) ? Path.GetFullPath(explicitDirectory) : null;

        foreach (var start in startDirectories.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var dir = new DirectoryInfo(start!);
            for (var depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, DirectoryName);
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.env").Any())
                    return candidate;
            }
        }
        return null;
    }

    public static string SettingsPath(string directory, string envName) => Path.Combine(directory, envName + ".env");

    public static string SecretsPath(string directory, string envName) => Path.Combine(directory, envName + ".secrets.env");

    /// <summary>
    /// Loads env/&lt;name&gt;.env (required) and env/&lt;name&gt;.secrets.env (optional; secrets may come from real
    /// environment variables instead). Secrets override settings.
    /// </summary>
    public static EnvFileSet Load(string envName, string directory)
    {
        var settings = SettingsPath(directory, envName);
        if (!File.Exists(settings))
            throw new FileNotFoundException(
                $"Environment settings file '{settings}' not found. PEMS_ENV='{envName}' needs env/{envName}.env " +
                $"(see env/README.md).", settings);

        var values = new Dictionary<string, string>(Parse(File.ReadAllLines(settings)), StringComparer.OrdinalIgnoreCase);
        var loaded = new List<string> { settings };

        var secrets = SecretsPath(directory, envName);
        var secretsFound = File.Exists(secrets);
        if (secretsFound)
        {
            foreach (var kv in Parse(File.ReadAllLines(secrets)))
                values[kv.Key] = kv.Value;
            loaded.Add(secrets);
        }

        return new EnvFileSet(envName, directory, values, loaded, secretsFound);
    }
}

public sealed record EnvFileSet(
    string EnvName,
    string Directory,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> LoadedFiles,
    bool SecretsFileFound);
