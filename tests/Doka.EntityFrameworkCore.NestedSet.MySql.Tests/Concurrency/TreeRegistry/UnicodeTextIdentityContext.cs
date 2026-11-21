using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Uses the native utf8mb4 character bound without a model converter.</summary>
internal sealed class UnicodeTextIdentityContext
    : UnicodeIdentityContext<string>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public UnicodeTextIdentityContext(
        DbContextOptions<UnicodeTextIdentityContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<string> property
    ) => property
        .HasMaxLength(12)
        .UseCollation("utf8mb4_bin");
}
