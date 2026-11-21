using PEMS.Shared.Configuration;

namespace PEMS.Web.Configuration;

public static class PemsConfigurationExtensions
{
    /// <summary>
    /// Adds env/&lt;name&gt;.env and env/&lt;name&gt;.secrets.env as a configuration source. Precedence (low to high):
    /// appsettings.json &lt; env file &lt; secrets file &lt; real environment variables &lt; command line, so a server
    /// can still override any value with an environment variable.
    /// Returns null when no environment is selected (non-development host without PEMS_ENV): configuration then
    /// comes from real environment variables only.
    /// </summary>
    public static EnvFileSet? AddPemsEnvFiles(this ConfigurationManager configuration, string hostEnvironmentName, string contentRoot)
    {
        var name = EnvFiles.ResolveName(Environment.GetEnvironmentVariable(EnvFiles.EnvNameVariable), hostEnvironmentName);
        if (name is null)
            return null;

        var directory = EnvFiles.FindDirectory(
            Environment.GetEnvironmentVariable(EnvFiles.DirectoryVariable), contentRoot, AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException(
                $"No '{EnvFiles.DirectoryName}' folder found for PEMS_ENV='{name}' (searched from '{contentRoot}'). " +
                $"Set {EnvFiles.DirectoryVariable} or create env/{name}.env (see env/README.md).");

        var set = EnvFiles.Load(name, directory);

        // Insert before the environment-variable source so real environment variables keep the last word.
        var source = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
        {
            InitialData = set.Values.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value))
        };
        var index = configuration.Sources.ToList()
            .FindIndex(s => s.GetType().Name == "EnvironmentVariablesConfigurationSource");
        if (index < 0)
            configuration.Sources.Add(source);
        else
            configuration.Sources.Insert(index, source);

        return set;
    }

    /// <summary>A setting that must be defined in the environment files (no silent in-code default).</summary>
    public static string Required(this IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Missing required setting '{key}'. Define it in env/<name>.env (or env/<name>.secrets.env for secrets) " +
                $"or as the environment variable {key.Replace(":", "__")}.");
        return value;
    }

    public static int RequiredInt(this IConfiguration configuration, string key)
        => int.TryParse(configuration.Required(key), System.Globalization.NumberStyles.Integer,
               System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new InvalidOperationException($"Setting '{key}' must be an integer.");

    public static double RequiredDouble(this IConfiguration configuration, string key)
        => double.TryParse(configuration.Required(key), System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new InvalidOperationException($"Setting '{key}' must be a number.");

    public static bool RequiredBool(this IConfiguration configuration, string key)
        => bool.TryParse(configuration.Required(key), out var v)
            ? v
            : throw new InvalidOperationException($"Setting '{key}' must be true or false.");
}
