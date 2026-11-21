using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Captures public EF state without any ChangeTracker or EntityEntry detection call.</summary>
    private static PlannedEntryState[] CapturePlannedParentState(
        DbContext context
    )
    {
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        return updates
            .Entries
            .OrderBy(entry => entry.EntityType.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.GetCurrentValue<int>(entry.EntityType.FindProperty("Id")!))
            .Select(entry => new PlannedEntryState(
                entry.ToEntityEntry()
                    .Entity,
                entry.EntityState,
                entry
                    .EntityType
                    .GetProperties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new PlannedPropertyState(
                        property.Name,
                        entry.GetCurrentValue(property),
                        entry.GetOriginalValue(property),
                        entry.IsModified(property),
                        entry.IsConceptualNull(property),
                        property.IsKey() || property.IsForeignKey()
                            ? entry.GetRelationshipSnapshotValue(property)
                            : null))
                    .ToArray(),
                entry
                    .EntityType
                    .GetNavigations()
                    .OrderBy(navigation => navigation.Name, StringComparer.Ordinal)
                    .Select(navigation =>
                    {
                        var current = entry.GetCurrentValue(navigation);
                        var relationship = entry.GetRelationshipSnapshotValue(navigation);

                        return new PlannedNavigationState(
                            navigation.Name,
                            current,
                            navigation.IsCollection
                                ? null
                                : relationship,
                            navigation.IsCollection
                                ? CopyPlannedMembers(current)
                                : [],
                            navigation.IsCollection
                                ? CopyPlannedMembers(relationship)
                                : []);
                    })
                    .ToArray(),
                entry
                    .EntityType
                    .GetForeignKeys()
                    .OrderBy(ForeignKeyOrder, StringComparer.Ordinal)
                    .Select(foreignKey => new PlannedPrincipalState(
                        foreignKey,
                        updates
                            .FindPrincipal(entry, foreignKey)
                            ?.ToEntityEntry()
                            .Entity))
                    .ToArray(),
                entry
                    .EntityType
                    .GetReferencingForeignKeys()
                    .OrderBy(ForeignKeyOrder, StringComparer.Ordinal)
                    .Select(foreignKey => new PlannedDependentsState(
                        foreignKey,
                        updates
                            .GetDependents(entry, foreignKey)
                            .Select(dependent => dependent.ToEntityEntry()
                                .Entity)
                            .ToArray()))
                    .ToArray()))
            .ToArray();
    }
}
