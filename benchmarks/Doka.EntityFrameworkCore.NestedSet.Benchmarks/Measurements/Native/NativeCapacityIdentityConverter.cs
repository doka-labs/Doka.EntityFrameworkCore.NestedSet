using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Shares the fixture's reversible scalar mapping between properties and unbound catalog literals.</summary>
internal sealed class NativeCapacityIdentityConverter : ValueConverter<Guid, int>
{
    /// <summary>Creates EF's parameterless converter for the restricted fixture identity domain.</summary>
    public NativeCapacityIdentityConverter()
        : base(
            value => NativeCapacityConsumerModel.Integer(value),
            value => NativeCapacityConsumerModel.Identity(value)) { }
}
