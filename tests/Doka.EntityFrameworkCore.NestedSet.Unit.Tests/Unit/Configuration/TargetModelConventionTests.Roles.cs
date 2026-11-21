namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TargetModelConventionTests
{
    /// <summary>Rejects Int32 boundaries selected through the untyped shadow-property surface.</summary>
    [Fact]
    public void FinalizeModel_Int32ShadowBoundary_IsRejectedAtFinalization()
    {
        // Arrange
        using var context = new InvalidShadowModelContext();

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("must be required Int64", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a TreeId whose value is generated instead of assigned by the hierarchy protocol.</summary>
    [Fact]
    public void FinalizeModel_GeneratedTreeId_IsRejectedAtFinalization()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(entity, false, null);
            entity
                .Property(node => node.TreeId)
                .ValueGeneratedOnAdd();
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("TreeId", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("writable and not generated", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a TreeId that EF treats as read-only after the node was inserted.</summary>
    [Fact]
    public void FinalizeModel_ReadOnlyTreeId_IsRejectedAtFinalization()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(entity, false, null);
            entity
                .Property(node => node.TreeId)
                .Metadata
                .SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("TreeId", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("writable and not generated", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects every invalid structural role type or nullable arithmetic role with a precise diagnosis.
    /// </summary>
    /// <param name="variant">The deliberately invalid role mapping.</param>
    /// <param name="role">The structural role expected in the diagnostic.</param>
    /// <param name="diagnostic">The role-specific contract fragment expected in the diagnostic.</param>
    [Theory]
    [InlineData("parent-string", "Parent", "nullable Parent property")]
    [InlineData("right-int", "Right", "required Int64")]
    [InlineData("depth-long", "Depth", "required Int32")]
    [InlineData("position-int", "Position", "required Int64")]
    [InlineData("tree-nullable", "TreeId", "must be required")]
    [InlineData("left-nullable", "Left", "required Int64")]
    [InlineData("right-nullable", "Right", "required Int64")]
    [InlineData("depth-nullable", "Depth", "required Int32")]
    [InlineData("position-nullable", "Position", "required Int64")]
    public void FinalizeModel_InvalidStructuralRoleType_IsRejectedAtFinalization(
        string variant,
        string role,
        string diagnostic
    )
    {
        // Arrange
        using var context = new InvalidRoleModelContext(variant);

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(role, invalid.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostic, invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects generated and read-only structural roles before a database connection is opened.</summary>
    /// <param name="variant">The generated or read-only role mapping.</param>
    /// <param name="role">The structural role expected in the diagnostic.</param>
    [Theory]
    [InlineData("generated-parent", "Parent")]
    [InlineData("generated-left", "Left")]
    [InlineData("generated-right", "Right")]
    [InlineData("generated-depth", "Depth")]
    [InlineData("generated-position", "Position")]
    [InlineData("readonly-parent", "Parent")]
    [InlineData("readonly-left", "Left")]
    [InlineData("readonly-right", "Right")]
    [InlineData("readonly-depth", "Depth")]
    [InlineData("readonly-position", "Position")]
    public void FinalizeModel_NonWritableStructuralRole_IsRejectedAtFinalization(
        string variant,
        string role
    )
    {
        // Arrange
        using var context = new InvalidRoleModelContext(variant);

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(role, invalid.Message, StringComparison.Ordinal);
        Assert.Contains("writable and not generated", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a generated Scope while accepting EF key immutability after insertion.</summary>
    [Fact]
    public void FinalizeModel_GeneratedScope_IsRejectedAtFinalization()
    {
        // Arrange
        using var context = new InvalidRoleModelContext("generated-scope");

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("Scope", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("assigned value and not be generated", invalid.Message, StringComparison.Ordinal);
    }
}
