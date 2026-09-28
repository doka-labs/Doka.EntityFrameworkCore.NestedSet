namespace Doka.EntityFrameworkCore.NestedSet.Testing;

/// <summary>Keeps provider capability profiles aligned with the canonical test-image manifest.</summary>
internal static class DatabaseTestTargets
{
    private static readonly Dictionary<string, string> s_images = LoadImages();

    /// <summary>Gets the immutable MariaDB image shared by runtime, migration, and benchmark fixtures.</summary>
    internal static string MariaDbImage => s_images["mariadb"];

    /// <summary>Gets the immutable MySQL image shared by runtime, migration, and benchmark fixtures.</summary>
    internal static string MySqlImage => s_images["mysql"];

    /// <summary>Gets the immutable PostgreSQL image shared by runtime, migration, and benchmark fixtures.</summary>
    internal static string PostgreSqlImage => s_images["postgres"];

    /// <summary>Gets the immutable SQL Server image for the supported Linux x64 CI host.</summary>
    internal static string SqlServerImage => s_images["sqlserver"];

    /// <summary>Gets the MariaDB capability profile derived from the image's version tag.</summary>
    internal static MySqlServerVersion MariaDb { get; } = MySqlServerVersion.MariaDb(ImageVersion(MariaDbImage));

    /// <summary>Gets the MySQL capability profile derived from the image's version tag.</summary>
    internal static MySqlServerVersion MySql { get; } = MySqlServerVersion.MySql(ImageVersion(MySqlImage));

    /// <summary>Reads the small embedded Dockerfile once, preserving one owner for automated image updates.</summary>
    /// <returns>Image references keyed by their Docker stage names.</returns>
    private static Dictionary<string, string> LoadImages()
    {
        using var stream = typeof(DatabaseTestTargets).Assembly.GetManifestResourceStream("NestedSet.TestImages")
            ?? throw new InvalidOperationException("The embedded test-image manifest is missing.");

        using var reader = new StreamReader(stream);

        // ReSharper disable once MethodHasAsyncOverload
        // WHY: Static fixture configuration reads a tiny in-memory assembly resource once, before asynchronous IO.
        var manifest = reader.ReadToEnd();
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in manifest.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0
                || parts[0]
                    .StartsWith('#'))
            {
                continue;
            }

            if (parts is not ["FROM", _, "AS", _]
                || !parts[1]
                    .Contains("@sha256:", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Test images must use FROM <tag>@sha256:<digest> AS <engine>.");
            }

            images.Add(parts[3], parts[1]);
        }

        return images;
    }

    /// <summary>Derives Doka's capability version from the same tag that selects the running server.</summary>
    /// <param name="image">An image with a three-part version tag and immutable digest.</param>
    /// <returns>The server version used for SQL capability selection.</returns>
    private static Version ImageVersion(
        string image
    )
    {
        var tagStart = image.IndexOf(':', StringComparison.Ordinal) + 1;
        var digestStart = image.IndexOf('@', StringComparison.Ordinal);

        return Version.Parse(image.AsSpan(tagStart, digestStart - tagStart));
    }
}
