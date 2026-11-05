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
        var options = new DbContextOptionsBuilder<UserGroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        await using var context = new UserGroupContext(options);
        object payload = shape switch
        {
            "unknown-field" => new UserMetadataDefinition
            {
                FieldCode = "Unknown",
                ValueKind = "Text",
            },
            "unsupported-kind" => new UserMetadataDefinition
            {
                FieldCode = "Route",
                ValueKind = "Int32",
            },
            "reversed-period" => new GroupRelationPeriod
            {
                ParentId = 1,
                ChildId = 2,
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            "reversed-membership" => new UserGroupMembership
            {
                UserId = 1,
                UserGroupId = 2,
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            "reversed-local-role" => new MembershipRole
            {
                UserId = 1,
                UserGroupId = 2,
                RoleId = Guid.NewGuid(),
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            "reversed-global-role" => new UserRole
            {
                UserId = 1,
                RoleId = Guid.NewGuid(),
                FromDate = new DateOnly(2026, 10, 2),
                ToDate = new DateOnly(2026, 10, 1),
            },
            _ => new UserMetadata
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
        var error = Record.Exception(() => SupervisorPayloadValidation.Validate(context));

        // Assert
        if (invalid)
        {
            Assert.IsType<ArgumentException>(error);
        }
        else
        {
            Assert.Null(error);
        }

        Assert.NotEqual(
            ConnectionState.Open,
            context.Database.GetDbConnection()
                .State);
    }
}
