namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents the first independently stored measurement source.</summary>
internal sealed class CompositionInputOne
{
    /// <summary>Gets or sets the source row's application scope.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the measured node key.</summary>
    internal int NodeId { get; set; }

    /// <summary>Gets or sets the measurement date used to evaluate each direct edge.</summary>
    internal DateOnly Date { get; set; }

    /// <summary>Gets or sets the additive measurement.</summary>
    internal int Amount { get; set; }
}

/// <summary>Represents the second independently stored measurement source.</summary>
internal sealed class CompositionInputTwo
{
    /// <summary>Gets or sets the source row's application scope.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the measured node key.</summary>
    internal int NodeId { get; set; }

    /// <summary>Gets or sets the measurement date used to evaluate each direct edge.</summary>
    internal DateOnly Date { get; set; }

    /// <summary>Gets or sets the additive measurement.</summary>
    internal int Amount { get; set; }
}

/// <summary>Represents the third independently stored measurement source.</summary>
internal sealed class CompositionInputThree
{
    /// <summary>Gets or sets the source row's application scope.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the measured node key.</summary>
    internal int NodeId { get; set; }

    /// <summary>Gets or sets the measurement date used to evaluate each direct edge.</summary>
    internal DateOnly Date { get; set; }

    /// <summary>Gets or sets the additive measurement.</summary>
    internal int Amount { get; set; }
}
