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

        return ParseImages(manifest);
    }

    /// <summary>Validates the shared, FROM-only image source before any database resource is acquired.</summary>
    /// <param name="manifest">The canonical Dockerfile embedded by the consuming assembly.</param>
    /// <returns>Exactly one tagged and SHA-256-pinned image for each supported server stage.</returns>
    /// <exception cref="ArgumentNullException">The manifest is null.</exception>
    /// <exception cref="InvalidOperationException">A stage is malformed, unpinned, unknown, duplicated, or missing.</exception>
    internal static Dictionary<string, string> ParseImages(
        string manifest
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var images = new Dictionary<string, string>(4, StringComparer.Ordinal);
        foreach (var line in manifest.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0
                || parts[0].StartsWith('#'))
            {
                continue;
            }

            // WHY: Both consumers share a deliberately small FROM-only contract, not a general Dockerfile parser.
            if (parts is not ["FROM", _, "AS", _] || !HasPinnedTag(parts[1]))
            {
                throw new InvalidOperationException("Test images must use FROM <tag>@sha256:<digest> AS <engine>.");
            }

            if (parts[3] is not ("mysql" or "mariadb" or "postgres" or "sqlserver"))
            {
                throw new InvalidOperationException($"Unknown database-image stage '{parts[3]}'.");
            }

            if (!images.TryAdd(parts[3], parts[1]))
            {
                throw new InvalidOperationException($"Duplicate database-image stage '{parts[3]}'.");
            }
        }

        if (images.Count != 4)
        {
            throw new InvalidOperationException("The database-image manifest must contain all four server stages.");
        }

        return images;
    }

    /// <summary>Requires a version tag and an entire lowercase SHA-256 digest, including for registry-port references.</summary>
    /// <param name="image">The image reference from a FROM instruction.</param>
    /// <returns>Whether the reference has the immutable shape required by both container consumers.</returns>
    private static bool HasPinnedTag(
        string image
    )
    {
        const string digestMarker = "@sha256:";
        var digestStart = image.IndexOf(digestMarker, StringComparison.Ordinal);
        if (digestStart < 0 || image.Length - digestStart != digestMarker.Length + 64)
        {
            return false;
        }

        var reference = image.AsSpan(0, digestStart);
        var tagStart = reference.LastIndexOf(':');
        if (tagStart <= reference.LastIndexOf('/') || tagStart == reference.Length - 1 || reference.Contains('@'))
        {
            return false;
        }

        foreach (var character in image.AsSpan(digestStart + digestMarker.Length))
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Derives Doka's capability version from the same tag that selects the running server.</summary>
    /// <param name="image">An image with a three-part version tag and immutable digest.</param>
    /// <returns>The server version used for SQL capability selection.</returns>
    internal static Version ImageVersion(
        string image
    )
    {
        var digestStart = image.IndexOf('@', StringComparison.Ordinal);
        // WHY: A registry port contains an earlier colon; the final colon before the digest identifies the version tag.
        var tagStart = image.AsSpan(0, digestStart).LastIndexOf(':') + 1;

        return Version.Parse(image.AsSpan(tagStart, digestStart - tagStart));
    }
}
