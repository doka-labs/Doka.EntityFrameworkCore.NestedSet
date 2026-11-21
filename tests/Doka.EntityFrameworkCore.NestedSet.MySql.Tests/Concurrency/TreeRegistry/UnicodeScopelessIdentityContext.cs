using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Exercises the identical TreeId guard without an optional Scope mapping.</summary>
internal sealed class UnicodeScopelessIdentityContext
    : UnicodeIdentityContext<string>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public UnicodeScopelessIdentityContext(
        DbContextOptions<UnicodeScopelessIdentityContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override bool Scoped => false;

    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<string> property
    ) => property
        .HasMaxLength(12)
        .UseCollation("utf8mb4_bin");
}
