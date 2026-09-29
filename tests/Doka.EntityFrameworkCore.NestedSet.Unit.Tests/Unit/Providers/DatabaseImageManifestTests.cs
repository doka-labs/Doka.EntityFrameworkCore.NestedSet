namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards the shared image source before tests or developer containers acquire a server.</summary>
public sealed class DatabaseImageManifestTests
{
    /// <summary>Comments, blank lines, CRLF, and stage order preserve all four embedded vendor references.</summary>
    [Fact]
    public void CompleteManifestPreservesEveryEmbeddedImage()
    {
        // Arrange
        var manifest = $"""
                        # Shared development and fixture images.

                        FROM {DatabaseTestTargets.SqlServerImage} AS sqlserver
                        FROM {DatabaseTestTargets.PostgreSqlImage} AS postgres
                        FROM {DatabaseTestTargets.MariaDbImage} AS mariadb
                        FROM {DatabaseTestTargets.MySqlImage} AS mysql
                        """.Replace("\n", "\r\n", StringComparison.Ordinal);

        // Act
        var images = DatabaseTestTargets.ParseImages(manifest);

        // Assert
        Assert.Equal(4, images.Count);
        Assert.Equal(DatabaseTestTargets.MySqlImage, images["mysql"]);
        Assert.Equal(DatabaseTestTargets.MariaDbImage, images["mariadb"]);
        Assert.Equal(DatabaseTestTargets.PostgreSqlImage, images["postgres"]);
        Assert.Equal(DatabaseTestTargets.SqlServerImage, images["sqlserver"]);
    }

    /// <summary>A registry port does not substitute for the image's required version tag.</summary>
    [Fact]
    public void RegistryPortWithVersionTagIsAccepted()
    {
        // Arrange
        var reference = $"registry.example:5000/mysql:8.4.11@sha256:{new string('a', 64)}";
        var manifest = CompleteManifest().Replace(DatabaseTestTargets.MySqlImage, reference, StringComparison.Ordinal);

        // Act
        var images = DatabaseTestTargets.ParseImages(manifest);
        var version = DatabaseTestTargets.ImageVersion(images["mysql"]);

        // Assert
        Assert.Equal(reference, images["mysql"]);
        Assert.Equal(new Version(8, 4, 11), version);
    }

    /// <summary>Doka's capability version always follows the tag selected by the actual embedded image.</summary>
    /// <param name="mariaDb">Whether to check the MariaDB or MySQL capability profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapabilityVersionMatchesEmbeddedImageTag(
        bool mariaDb
    )
    {
        // Arrange
        var image = mariaDb ? DatabaseTestTargets.MariaDbImage : DatabaseTestTargets.MySqlImage;
        var version = mariaDb ? DatabaseTestTargets.MariaDb : DatabaseTestTargets.MySql;
        var tag = image.Split(':', '@')[1];

        // Act
        var actual = version.Version.ToString();

        // Assert
        Assert.Equal(tag, actual);
        Assert.Equal(mariaDb, version.IsMariaDb);
    }

    /// <summary>Incomplete, ambiguous, and mutable manifests fail before any server starts.</summary>
    /// <param name="fault">The independently rejected manifest defect.</param>
    /// <param name="message">The diagnostic identifying that defect.</param>
    [Theory]
    [InlineData("MissingStage", "all four")]
    [InlineData("DuplicateStage", "Duplicate")]
    [InlineData("UnknownStage", "Unknown")]
    [InlineData("WrongStageCase", "Unknown")]
    [InlineData("NoDigest", "FROM")]
    [InlineData("NoVersionTag", "FROM")]
    [InlineData("RegistryPortWithoutTag", "FROM")]
    [InlineData("EmptyTag", "FROM")]
    [InlineData("ShortDigest", "FROM")]
    [InlineData("NonHexDigest", "FROM")]
    [InlineData("AdditionalInstruction", "FROM")]
    [InlineData("Empty", "all four")]
    public void InvalidManifestIsRejected(
        string fault,
        string message
    )
    {
        // Arrange
        var manifest = FaultyManifest(fault);

        // Act
        var error = Record.Exception(() => DatabaseTestTargets.ParseImages(manifest));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    /// <summary>A null manifest receives a direct argument error rather than a later resource failure.</summary>
    [Fact]
    public void NullManifestIsRejected()
    {
        // Arrange
        string? manifest = null;

        // Act
        var error = Record.Exception(() => DatabaseTestTargets.ParseImages(manifest!));

        // Assert
        Assert.IsType<ArgumentNullException>(error);
    }

    /// <summary>Builds test input from the actual pins so normal dependency updates do not require duplicate versions.</summary>
    private static string CompleteManifest() => $"""
                                               FROM {DatabaseTestTargets.MySqlImage} AS mysql
                                               FROM {DatabaseTestTargets.MariaDbImage} AS mariadb
                                               FROM {DatabaseTestTargets.PostgreSqlImage} AS postgres
                                               FROM {DatabaseTestTargets.SqlServerImage} AS sqlserver
                                               """;

    /// <summary>Introduces exactly one configuration defect into an otherwise complete image source.</summary>
    /// <param name="fault">The named defect selected by the regression case.</param>
    /// <returns>The malformed input submitted to the parser.</returns>
    private static string FaultyManifest(
        string fault
    )
    {
        var manifest = CompleteManifest();
        var digest = new string('a', 64);
        var image = fault switch
        {
            "NoDigest" => "mysql:8.4.11",
            "NoVersionTag" => $"mysql@sha256:{digest}",
            "RegistryPortWithoutTag" => $"registry.example:5000/mysql@sha256:{digest}",
            "EmptyTag" => $"mysql:@sha256:{digest}",
            "ShortDigest" => $"mysql:8.4.11@sha256:{digest[..63]}",
            "NonHexDigest" => $"mysql:8.4.11@sha256:{new string('g', 64)}",
            _ => DatabaseTestTargets.MySqlImage,
        };

        return fault switch
        {
            "MissingStage" => manifest.Replace(
                $"FROM {DatabaseTestTargets.SqlServerImage} AS sqlserver",
                "",
                StringComparison.Ordinal),
            "DuplicateStage" => $"{manifest}\nFROM {DatabaseTestTargets.MySqlImage} AS mysql",
            "UnknownStage" => manifest.Replace("AS sqlserver", "AS sqlite", StringComparison.Ordinal),
            "WrongStageCase" => manifest.Replace("AS mysql", "AS MySql", StringComparison.Ordinal),
            "AdditionalInstruction" => $"{manifest}\nRUN true",
            "Empty" => "# No configured engines.\n\n",
            _ => manifest.Replace(DatabaseTestTargets.MySqlImage, image, StringComparison.Ordinal),
        };
    }
}
