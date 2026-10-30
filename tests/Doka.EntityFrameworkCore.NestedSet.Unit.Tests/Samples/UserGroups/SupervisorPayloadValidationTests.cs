using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

/// <summary>
/// Validates the real sample's finite scalar catalog independently of database foreign-key enforcement.
/// </summary>
public sealed class SupervisorPayloadValidationTests
{
    /// <summary>Unknown fields, unsupported kinds, non-ASCII routing, and reversed periods fail before I/O.</summary>
    /// <param name="shape">The payload shape passed to the real sample validator.</param>
    /// <param name="invalid">Whether the payload must fail validation before a connection is opened.</param>
    [Theory]
    [InlineData("unknown-field", true)]
    [InlineData("unsupported-kind", true)]
    [InlineData("non-ascii", true)]
    [InlineData("reversed-period", true)]
    [InlineData("reversed-membership", true)]
    [InlineData("reversed-local-role", true)]
    [InlineData("reversed-global-role", true)]
    [InlineData("text", false)]
    [InlineData("null", false)]
    [InlineData("empty", false)]
    public async Task ExplicitPayloadPolicyRejectsInvalidFieldsAndPreservesMissingValues(
        string shape,
        bool invalid
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<App.UserGroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        await using var context = new App.UserGroupContext(options);
        object payload = shape switch
        {
            "unknown-field" => new App.UserMetadataDefinition
            {
                FieldCode = "Unknown",
                ValueKind = "Text",
            },
            "unsupported-kind" => new App.UserMetadataDefinition
            {
                FieldCode = "Route",
                ValueKind = "Int32",
            },
            "reversed-period" => new App.GroupRelationPeriod
            {
                ParentId = 1,
                ChildId = 2,
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            "reversed-membership" => new App.UserGroupMembership
            {
                UserId = 1,
                UserGroupId = 2,
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            "reversed-local-role" => new App.MembershipRole
            {
                UserId = 1,
                UserGroupId = 2,
                RoleId = Guid.NewGuid(),
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            "reversed-global-role" => new App.UserRole
            {
                UserId = 1,
                RoleId = Guid.NewGuid(),
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            _ => new App.UserMetadata
            {
                UserId = 1,
                FieldCode = "Route",
                Value = shape switch
                {
                    "non-ascii" => "caf\u00e9",
                    "null" => null,
                    "empty" => string.Empty,
                    _ => "lane-blue ",
                },
            },
        };

        await context.AddAsync(payload, CancellationToken.None);

        // Act
        var error = Record.Exception(() => App.SupervisorPayloadValidation.Validate(context));

        // Assert
        if (invalid)
        {
            Assert.IsType<ArgumentException>(error);
        }
        else
        {
            Assert.Null(error);
        }

        Assert.NotEqual(ConnectionState.Open, context.Database.GetDbConnection().State);
    }
}
