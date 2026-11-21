using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>
    /// Groups the arranged caller objects and their existing entry identities without owning resources.
    /// </summary>
    private readonly record struct PendingForeignKeyGraph(
        RollbackOwner Original,
        RollbackOwner Transient,
        RollbackOwner Reviewer,
        RollbackNode Node,
        EntityEntry<RollbackNode> Entry
    );

    /// <summary>Keeps independently captured scalar lanes separate from inverse membership.</summary>
    private readonly record struct ForeignKeyValues(
        object?[] Installed,
        object?[] Current,
        bool[] Modified,
        object?[] Original
    );

    /// <summary>Keeps exact CLR containers and native membership before either representation is mutated.</summary>
    private readonly record struct ForeignKeyMembership(
        RollbackOwner? Reference,
        object? InstalledReference,
        List<RollbackNode> OriginalCollection,
        RollbackNode[] OriginalMembers,
        List<RollbackNode> TransientCollection,
        RollbackNode[] TransientMembers,
        object[] NativeOriginal,
        object[] NativeTransient,
        RollbackNode[] ReviewerMembers
    );

    /// <summary>Combines native identities with their preimage and the actual rollback journal under test.</summary>
    private readonly record struct ForeignKeyFrame(
        IUpdateEntry Update,
        IUpdateEntry OriginalUpdate,
        IUpdateEntry TransientUpdate,
        IUpdateAdapter Updates,
        INavigation Reference,
        INavigation Inverse,
        ForeignKeyValues Values,
        ForeignKeyMembership Membership,
        NestedSetTrackerSnapshot Snapshot
    );

    /// <summary>Records the callback-installed FK and bucket while they are still deliberately divergent.</summary>
    private readonly record struct HiddenForeignKey(
        object?[] Installed,
        bool Bucket
    );
}
