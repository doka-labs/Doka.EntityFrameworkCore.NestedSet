namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Maps one explicitly named registry for provider-generated runtime models.</summary>
public sealed class TreeRegistryCompiledContext : NestedSetDbContext
{
    /// <summary>The physical name the generated model has to preserve.</summary>
    internal const string RegistryTable = "Compiled_HierarchyTrees";

    /// <summary>Creates a context with the exact provider or generated model selected by the test.</summary>
    /// <param name="options">The actual provider options and optional compiled model.</param>
    public TreeRegistryCompiledContext(
        DbContextOptions<TreeRegistryCompiledContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TreeRegistryCompiledNode>();
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node.HasNestedSet(builder => builder
            .HasParent(value => value.ParentId)
            .HasTreeRegistryTable(RegistryTable));
    }
}
