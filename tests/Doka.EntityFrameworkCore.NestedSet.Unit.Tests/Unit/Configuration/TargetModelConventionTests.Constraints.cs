namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TargetModelConventionTests
{
    /// <summary>Reuses an application check that has the generated name and identical SQL.</summary>
    [Fact]
    public void DesignEntity_EquivalentNamedCheckConstraint_IsReused()
    {
        // Arrange
        using var baseline = ValidTargetContext();
        var leftCheck = DesignEntity(baseline)
            .GetCheckConstraints()
            .Single(constraint => constraint.Sql == "\"Left\" >= 1");

        var leftCheckName = leftCheck.Name
            ?? throw new InvalidOperationException("The generated Left check constraint did not have a physical name.");

        using var context = CheckConstraintContext(leftCheckName, leftCheck.Sql);

        // Act
        var entity = DesignEntity(context);

        // Assert
        var actual = Assert.Single(entity.GetCheckConstraints(), constraint => constraint.Name == leftCheckName);
        Assert.Equal(leftCheck.Sql, actual.Sql);
    }

    /// <summary>Rejects an application check that reuses a generated name for different SQL.</summary>
    [Fact]
    public void FinalizeModel_ConflictingNamedCheckConstraint_IsRejected()
    {
        // Arrange
        using var baseline = ValidTargetContext();
        var leftCheck = DesignEntity(baseline)
            .GetCheckConstraints()
            .Single(constraint => constraint.Sql == "\"Left\" >= 1");

        var leftCheckName = leftCheck.Name
            ?? throw new InvalidOperationException("The generated Left check constraint did not have a physical name.");

        using var context = CheckConstraintContext(leftCheckName, "\"Left\" >= 0");

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(leftCheckName, invalid.Message, StringComparison.Ordinal);
        Assert.Contains("conflicts with application SQL", invalid.Message, StringComparison.Ordinal);
    }
}
