using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Keeps converted tree identities consistent between properties and unbound status-union literals.</summary>
internal sealed class CompositionConsumerTreeConverter : ValueConverter<Guid, Guid>
{
    /// <summary>Creates the bijective swap used by this fixture's logical and physical tree identities.</summary>
    public CompositionConsumerTreeConverter()
        : base(value => CompositionConsumerModel.SwapTree(value), value => CompositionConsumerModel.SwapTree(value)) { }
}
