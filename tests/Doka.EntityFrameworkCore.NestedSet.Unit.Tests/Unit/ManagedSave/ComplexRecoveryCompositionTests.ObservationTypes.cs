using TopologyDetails = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyDetails;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexRecoveryCompositionTests
{
    /// <summary>Retains independently observed CLR and native state without invoking production verification.</summary>
    private sealed record Preimage(
        ClrObservation Clr,
        NativeObservation[] Native
    );

    /// <summary>Retains top-level caller-owned containers and independent scalar copies.</summary>
    private sealed record ClrObservation(
        string Marker,
        List<TopologyItem>? Rows,
        ClrItemObservation[] Items,
        ClrDetailsObservation Optional
    );

    /// <summary>Retains one caller element and its optional and nested scalar preimages.</summary>
    private sealed record ClrItemObservation(
        TopologyItem Instance,
        string Text,
        ClrDetailsObservation Details,
        List<TopologyLeaf>? Leaves,
        ClrLeafObservation[] Values
    );

    /// <summary>Retains an exact caller leaf and its immutable scalar preimage.</summary>
    private sealed record ClrLeafObservation(
        TopologyLeaf Instance,
        string Text
    );

    /// <summary>Retains nullable optional-complex identity and immutable scalar values.</summary>
    private sealed record ClrDetailsObservation(
        TopologyDetails? Instance,
        string? Text,
        string? Code
    );

    /// <summary>Retains native identity, lifecycle state, scalar flags and both collection lanes.</summary>
    private sealed record NativeObservation(
        object Owner,
        ITypeBase Metadata,
        string Path,
        EntityState State,
        int? Ordinal,
        int? OriginalOrdinal,
        NativeScalarObservation[] Scalars,
        NativeCollectionObservation[] Collections
    );

    /// <summary>Retains public current/original scalar values and independent native scalar flags.</summary>
    private sealed record NativeScalarObservation(
        IProperty Metadata,
        bool HasCurrent,
        object? Current,
        object? Original,
        bool Modified,
        bool Temporary
    );

    /// <summary>Retains exact current and original slot membership, including lengths and null holes.</summary>
    private sealed record NativeCollectionObservation(
        IComplexProperty Metadata,
        object?[] Current,
        object?[] Original
    );
}
