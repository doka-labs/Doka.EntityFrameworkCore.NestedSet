namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Names only the reporting sample's shadow structural roles.</summary>
internal static class SignalStorage
{
    /// <summary>Names the stable tree identity.</summary>
    internal const string TreeId = "NestedSetTreeId";

    /// <summary>Names the nullable direct parent identity.</summary>
    internal const string ParentId = "NestedSetParentId";

    /// <summary>Names the inclusive left boundary.</summary>
    internal const string Left = "NestedSetLeft";

    /// <summary>Names the inclusive right boundary.</summary>
    internal const string Right = "NestedSetRight";

    /// <summary>Names the structural depth.</summary>
    internal const string Depth = "NestedSetDepth";

    /// <summary>Names the direct sibling position.</summary>
    internal const string Position = "NestedSetPosition";
}
