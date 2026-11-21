using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares the fixture's reversible scalar mapping between properties and unbound catalog literals.</summary>
internal sealed class CompositionConsumerIdentityConverter : ValueConverter<Guid, int>
{
    /// <summary>Creates EF's parameterless converter for the restricted fixture identity domain.</summary>
    public CompositionConsumerIdentityConverter()
        : base(value => CompositionConsumerModel.Integer(value), value => CompositionConsumerModel.Identity(value)) { }
}
