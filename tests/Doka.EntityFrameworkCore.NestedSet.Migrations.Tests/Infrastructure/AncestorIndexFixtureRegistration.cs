namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Shares one lazily started engine per provider across ancestor-index migration scenarios.</summary>
/// <remarks>Serial collection execution also protects the fixture's engine cache during first use.</remarks>
[CollectionDefinition(Name)]
public sealed class AncestorIndexFixtureRegistration : ICollectionFixture<MigrationFixture>
{
    /// <summary>The xUnit collection used by the related migration, lifecycle, and generated-model suites.</summary>
    public const string Name = "AncestorIndexMigrations";
}
