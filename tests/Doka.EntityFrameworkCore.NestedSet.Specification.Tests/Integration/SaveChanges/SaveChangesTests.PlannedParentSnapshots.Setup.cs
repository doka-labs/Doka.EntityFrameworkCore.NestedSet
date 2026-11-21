using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Constructs the caller's graph without letting a public entry accessor normalize it prematurely.
    /// </summary>
    private static async Task<PlannedParentGraph> PreparePlannedParentAsync(
        DbContext context,
        string navigation,
        bool automaticDetection,
        bool pendingPayload,
        bool normalizeAutomatic,
        CancellationToken cancellationToken
    )
    {
        var nodes = await context
            .Set<NavigationNode>()
            .Include(node => node.Children)
            .Include(node => node.ScopeOwner)
            .ToArrayAsync(cancellationToken);

        var root = nodes.Single(node => node.Id == 1);
        var child = nodes.Single(node => node.Id == 2);
        var target = nodes.Single(node => node.Id == 3);
        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(cancellationToken);

        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        // WHY: Warm native dependent indexes while keys are unchanged, before any caller-side raw FK edit.
        foreach (var entry in updates.Entries)
        {
            foreach (var foreignKey in entry.EntityType.GetReferencingForeignKeys())
            {
                _ = updates
                    .GetDependents(entry, foreignKey)
                    .ToArray();
            }
        }

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var noteValue = context
            .Entry(note)
            .Property(value => value.Value);

        child.ParentId = target.Id;
        context
            .Entry(child)
            .Property(node => node.ParentId)
            .IsModified = true;

        if (navigation is "reference" or "both")
        {
            child.Parent = target;
        }

        if (navigation is "add" or "collections" or "both")
        {
            target.Children.Add(child);
        }

        if (navigation is "remove" or "collections" or "both")
        {
            root.Children.Remove(child);
        }

        if (pendingPayload)
        {
            noteValue.CurrentValue = "planned payload";
        }

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (automaticDetection && normalizeAutomatic)
        {
            // WHY: Automatic rollback baselines describe detected state, as the existing tracker suite does.
            context.ChangeTracker.DetectChanges();
        }

        return new PlannedParentGraph(root, child, target, noteValue);
    }

    /// <summary>
    ///     Creates the destination tree without loading any hierarchy input into the later facade context.
    /// </summary>
    private static async Task SeedPreparedNavigationDestinationAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await using var setup = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: cancellationToken);

        await setup
            .NestedSet<NavigationNode>()
            .ForScope(1)
            .InsertRootAsync(
                new NavigationNode
                {
                    Id = 100,
                    Name = "Destination",
                },
                s_manualDestinationTree,
                cancellationToken);
    }

    /// <summary>Reuses the existing insertion-shape data with the navigation model's explicit scope.</summary>
    private static NavigationNode[] PreparedNavigationInputs(
        string shape,
        int scope
    )
        => ManualInsertionInputs(shape)
            .Select(input => new NavigationNode
            {
                Id = input.Id,
                Scope = scope,
                Name = input.Name ?? throw new InvalidOperationException("The insertion fixture requires names."),
            })
            .ToArray();
}
