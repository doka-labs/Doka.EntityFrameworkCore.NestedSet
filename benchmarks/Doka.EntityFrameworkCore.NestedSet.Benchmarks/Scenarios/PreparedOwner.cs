namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Provides a persisted owner whose collection can expose the complete detached import plan.</summary>
internal sealed class PreparedOwner
{
    /// <summary>Gets or sets the assigned owner key shared by every imported node.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets ordinary payload used to verify a later marked application save.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets the inverse collection whose prepared members cross bounded insertion waves.</summary>
    public List<PreparedOwnerNode> Nodes { get; } = [];
}
