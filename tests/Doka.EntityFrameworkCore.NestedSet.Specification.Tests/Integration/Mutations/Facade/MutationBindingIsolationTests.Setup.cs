namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class MutationBindingIsolationTests
{
    /// <summary>Completely resets only the property-bag hierarchy tables owned by the selected test model.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task ResetModelAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var entity in context
            .Model
            .GetEntityTypes()
            .Where(entity => entity.ClrType == typeof(Dictionary<string, object>)))
        {
            var rows = context.Set<Dictionary<string, object>>(entity.Name);

            if (entity.FindProperty(Parent)!.ClrType == typeof(int?))
            {
                await rows.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => EF.Property<int?>(row, Parent), (int?)null),
                    cancellationToken);
            }
            else
            {
                await rows.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => EF.Property<Guid?>(row, Parent), (Guid?)null),
                    cancellationToken);
            }

            await rows.ExecuteDeleteAsync(cancellationToken);
        }

        await context.ClearNestedSetTreeRegistriesAsync(cancellationToken);
    }

    /// <summary>Initializes explicit property-bag roles without relying on missing-indexer defaults.</summary>
    private static Dictionary<string, object> Bag(
        object id,
        object scope,
        object tree
    ) => new()
    {
        [Id] = id,
        [Scope] = scope,
        [Tree] = tree,
        [Parent] = null!,
        [Left] = 0L,
        [Right] = 0L,
        [Depth] = 0,
        [Position] = 0L,
    };

    /// <summary>Creates deterministic Guid node keys distinct from the integer mapping's native keys.</summary>
    private static Guid GuidKey(
        int ordinal
    ) => Guid.Parse($"45000000-0000-0000-0000-{ordinal:D12}");

    /// <summary>Reads the integer/Guid/int mapping through its own property-bag role types.</summary>
    private static (int Id, int Scope, Guid Tree, int? Parent, long Left, long Right, int Depth, long Position)
        AlphaRow(
            Dictionary<string, object> row
        ) => ((int)row[Id], (int)row[Scope], (Guid)row[Tree], (int?)row[Parent], (long)row[Left], (long)row[Right],
        (int)row[Depth], (long)row[Position]);

    /// <summary>Reads the Guid/string/string mapping through its independent property-bag role types.</summary>
    private static (Guid Id, string Scope, string Tree, Guid? Parent, long Left, long Right, int Depth, long Position)
        BetaRow(
            Dictionary<string, object> row
        ) => ((Guid)row[Id], (string)row[Scope], (string)row[Tree], (Guid?)row[Parent], (long)row[Left],
        (long)row[Right],
        (int)row[Depth], (long)row[Position]);

    /// <summary>Reads the registry owned solely by the integer-key named hierarchy.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<(int Scope, Guid Tree, byte Lifecycle)[]> AlphaRegistriesAsync(
        IsolationContext context,
        CancellationToken cancellationToken
    )
    {
        var registry = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(Alpha)!)
            .Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .OrderBy(row => EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope))
            .ThenBy(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId))
            .Select(row => new ValueTuple<int, Guid, byte>(
                EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope),
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Reads the independent registry owned solely by the Guid-key named hierarchy.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<(string Scope, string Tree, byte Lifecycle)[]> StringRegistriesAsync(
        DbContext context,
        string entityTypeName,
        CancellationToken cancellationToken
    )
    {
        var registry = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(entityTypeName)!)
            .Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .OrderBy(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope))
            .ThenBy(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.TreeId))
            .Select(row => new ValueTuple<string, string, byte>(
                EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope),
                EF.Property<string>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Configures shared coordinates while each exact model retains its own scalar identity roles.</summary>
    private static void ConfigureCoordinates(
        EntityTypeBuilder<Dictionary<string, object>> node
    )
    {
        node.IndexerProperty<long>(Left);
        node.IndexerProperty<long>(Right);
        node.IndexerProperty<int>(Depth);
        node.IndexerProperty<long>(Position);
        node.HasKey(Id);
        node.HasNestedSet(builder => builder
            .HasNodeKey(Id)
            .HasTreeId(Tree)
            .HasScope(Scope)
            .HasParent(Parent)
            .HasBounds(Left, Right)
            .HasDepth(Depth)
            .HasPosition(Position));
    }
}
