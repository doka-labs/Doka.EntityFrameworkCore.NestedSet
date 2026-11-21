using System.Buffers.Binary;
using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;
using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Adapts actual sample models to the already persisted capacity nodes without copying a hierarchy.</summary>
/// <remarks>
/// WHY: The consumers intentionally use different CLR keys. Reversible fixture conversions keep their real
/// builders under test while structural reads still seek the original integer primary key. These read-only
/// views qualify query scale, not application migrations, period-table indexes, or writes through a view.
/// </remarks>
internal static class NativeCapacityConsumerModel
{
    /// <summary>Names the read-only signal projection over the shared structural table.</summary>
    internal const string SignalNodes = "CCL_Nodes";

    /// <summary>Names the exact-edge validity projection used only by capacity reads.</summary>
    internal const string SignalPeriods = "CCL_Periods";

    /// <summary>Names the read-only group projection over those same structural rows.</summary>
    internal const string GroupNodes = "CCG_Nodes";

    /// <summary>Gets the one authorized logical tenant, stored as the original scope integer.</summary>
    internal static readonly Guid Tenant = Identity(1);

    /// <summary>Gets the nonempty logical tree identity swapped with the fixture's physical empty GUID.</summary>
    internal static readonly Guid Tree = Identity(2);

    /// <summary>Gets the inclusive first day for both measurement and projected validity data.</summary>
    internal static readonly DateOnly FirstDay = new(2042, 3, 10);

    private static readonly NativeCapacityIdentityConverter s_identity = new();

    private static readonly NativeCapacityTreeConverter s_tree = new();

    /// <summary>Creates one allocation-free, reversible fixture identity in the documented integer range.</summary>
    internal static Guid Identity(
            int value
        )

        // WHY: Unbound catalog sentinels retain Guid.Empty <-> zero; real fixture keys use the
        // sample's independent synthetic namespace, including keys produced by its catalog seed.
        => value == 0
            ? Guid.Empty
            : SignalApp.SignalReportDataset.Id(value);

    /// <summary>Rejects accidental arbitrary GUIDs instead of silently collapsing distinct application keys.</summary>
    internal static int Integer(
        Guid value
    )
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        var integer = BinaryPrimitives.ReadInt32LittleEndian(bytes);

        if (value != Identity(integer))
        {
            throw new ArgumentException(
                "Consumer capacity identities must use the reversible fixture range.",
                nameof(value));
        }

        return integer;
    }

    /// <summary>Swaps the two tree identities bijectively, retaining every other identity unchanged.</summary>
    internal static Guid SwapTree(
        Guid value
    ) => value == Guid.Empty
        ? Tree
        : value == Tree
            ? Guid.Empty
            : value;

    /// <summary>Applies consistent physical types to catalog unions, foreign keys, and node joins.</summary>
    internal static void Configure(
        ModelBuilder model,
        bool signal
    )
    {
        foreach (var entity in model
            .Model
            .GetEntityTypes()
            .ToArray())
        {
            var table = entity.GetTableName();

            if (table is not null)
            {
                entity.SetTableName(
                    (signal
                        ? "CCL_"
                        : "CCG_")
                    + table);
            }

            foreach (var property in entity.GetDeclaredProperties())
            {
                if (property.ClrType != typeof(Guid)
                    && property.ClrType != typeof(Guid?))
                {
                    continue;
                }

                var tree = property.Name is "TreeId" or "NestedSetTreeId";
                property.SetValueConverter(
                    tree
                        ? s_tree
                        : s_identity);
            }
        }

        if (signal)
        {
            model
                .Entity<SignalApp.SignalNode>()
                .ToTable(SignalNodes);

            model
                .Entity<SignalApp.LinkWindow>()
                .ToTable(SignalPeriods);
        }
        else
        {
            model
                .Entity<GroupApp.UserGroup>()
                .ToTable(GroupNodes);

            model
                .Entity<GroupApp.UserGroup>()
                .Property(row => row.Id)
                .ValueGeneratedNever();

            model
                .Entity<GroupApp.User>()
                .Property(row => row.Id)
                .ValueGeneratedNever();
        }
    }

}
