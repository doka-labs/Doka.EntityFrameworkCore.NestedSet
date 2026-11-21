using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Keeps converted tree identities consistent between properties and unbound status-union literals.</summary>
internal sealed class NativeCapacityTreeConverter : ValueConverter<Guid, Guid>
{
    /// <summary>Creates the bijective swap used by this fixture's logical and physical tree identities.</summary>
    public NativeCapacityTreeConverter()
        : base(
            value => NativeCapacityConsumerModel.SwapTree(value),
            value => NativeCapacityConsumerModel.SwapTree(value)) { }
}
