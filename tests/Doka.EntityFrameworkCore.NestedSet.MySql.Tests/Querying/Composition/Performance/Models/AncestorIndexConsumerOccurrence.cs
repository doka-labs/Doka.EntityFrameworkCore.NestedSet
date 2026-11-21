namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Represents a consumer occurrence independently of the selected hierarchy node.</summary>
internal sealed class AncestorIndexConsumerOccurrence
{
    /// <summary>Gets or sets the occurrence identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the referenced node key.</summary>
    public int Node { get; set; }

    /// <summary>Gets or sets the ordinary consumer selection label.</summary>
    public string Selection { get; set; } = string.Empty;
}
