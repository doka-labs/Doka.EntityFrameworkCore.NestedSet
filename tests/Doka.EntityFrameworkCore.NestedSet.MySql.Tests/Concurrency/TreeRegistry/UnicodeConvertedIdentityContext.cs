using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Uses the same native character bound after converting a model value object.</summary>
internal sealed class UnicodeConvertedIdentityContext
    : UnicodeIdentityContext<UnicodeIdentityValue>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public UnicodeConvertedIdentityContext(
        DbContextOptions<UnicodeConvertedIdentityContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<UnicodeIdentityValue> property
    ) => property
        .HasConversion(value => value.Value, value => new UnicodeIdentityValue(value))
        .HasMaxLength(12)
        .UseCollation("utf8mb4_bin");
}
