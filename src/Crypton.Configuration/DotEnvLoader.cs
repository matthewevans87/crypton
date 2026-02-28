namespace Crypton.Configuration;

/// <summary>
/// Loads a .env file into environment variables before the host builder runs,
/// so the values flow into IConfiguration via AddEnvironmentVariables().
///
/// Precedence (highest → lowest):
///   1. Real environment variables (e.g. set by Docker / the OS)
///   2. .env file values loaded by this class
///   3. appsettings.json defaults
///
/// Existing environment variables are never overwritten, so a real env var
/// always wins over a .env file entry.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Searches for a .env file starting from <paramref name="startDirectory"/> and
    /// walking up the directory tree. Does nothing if no file is found.
    /// </summary>
    public static void Load(string? startDirectory = null)
    {
        var path = FindEnvFile(startDirectory ?? Directory.GetCurrentDirectory());
        if (path is null) return;
        Load(new FileInfo(path));
    }

    /// <summary>Loads a specific .env file.</summary>
    public static void Load(FileInfo file)
    {
        if (!file.Exists) return;

        foreach (var line in File.ReadAllLines(file.FullName))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();

            // Strip optional surrounding quotes
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            // Don't override variables that are already set in the real environment
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindEnvFile(string directory)
    {
        var candidate = Path.Combine(directory, ".env");
        if (File.Exists(candidate)) return candidate;

        var parent = Directory.GetParent(directory);
        return parent is null ? null : FindEnvFile(parent.FullName);
    }
}
