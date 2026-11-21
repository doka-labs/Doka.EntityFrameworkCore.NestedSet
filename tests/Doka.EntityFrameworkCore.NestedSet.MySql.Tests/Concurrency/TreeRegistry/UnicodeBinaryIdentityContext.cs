using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Uses a byte bound after converting text identities to their actual UTF-8 storage.</summary>
internal sealed class UnicodeBinaryIdentityContext
    : UnicodeIdentityContext<string>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public UnicodeBinaryIdentityContext(
        DbContextOptions<UnicodeBinaryIdentityContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void ConfigureIdentity(
        PropertyBuilder<string> property
    ) => property
        .HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value))
        .HasMaxLength(48)
        .HasColumnType("varbinary(48)");
}
