namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Validates explicit supervisor request alternatives without starting a provider query.</summary>
public sealed class SupervisorRequestTests
{
    /// <summary>
    /// Every documented role alternative is explicit and accepted with a valid identity when required.
    /// </summary>
    /// <param name="role">The explicit role-source alternative.</param>
    [Theory]
    [InlineData("none")]
    [InlineData("membership")]
    [InlineData("global")]
    [InlineData("either")]
    public void ExplicitRoleAlternativeIsAccepted(string role)
    {
        // Arrange
        var request = ValidRequest() with
        {
            Roles = Role(role, new Guid("11111111-1111-1111-1111-111111111111")),
        };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.Null(error);
    }

    /// <summary>An absent or empty role identity never silently disables role filtering.</summary>
    /// <param name="role">The role alternative with a missing, empty, or unsupported identity.</param>
    [Theory]
    [InlineData("membership")]
    [InlineData("global")]
    [InlineData("either")]
    [InlineData("unknown")]
    [InlineData("null")]
    public void InvalidRoleAlternativeIsRejected(string role)
    {
        // Arrange
        var request = ValidRequest() with { Roles = Role(role, Guid.Empty) };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>Group selection accepts all groups or an explicit positive one-based group rank.</summary>
    /// <param name="group">The explicit group-selection alternative.</param>
    [Theory]
    [InlineData("all")]
    [InlineData("eligible")]
    [InlineData("type")]
    public void ExplicitGroupAlternativeIsAccepted(string group)
    {
        // Arrange
        var request = ValidRequest() with { Groups = Groups(group, 1) };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.Null(error);
    }

    /// <summary>
    /// Zero and negative group ranks, null type collections, and unknown alternatives fail before I/O.
    /// </summary>
    /// <param name="group">The malformed group-selection alternative.</param>
    /// <param name="rank">The requested one-based group rank.</param>
    [Theory]
    [InlineData("eligible", 0)]
    [InlineData("eligible", -1)]
    [InlineData("type", 0)]
    [InlineData("type", -1)]
    [InlineData("null-types", 1)]
    [InlineData("null-eligible-types", 1)]
    [InlineData("unknown", 1)]
    [InlineData("null", 1)]
    public void InvalidGroupAlternativeIsRejected(
        string group,
        int rank
    )
    {
        // Arrange
        var request = ValidRequest() with { Groups = Groups(group, rank) };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>Person slots are positive and belong only to the person-rank alternative.</summary>
    /// <param name="kind">The person-selection kind whose rank contract is checked.</param>
    /// <param name="number">The invalid, missing, or inapplicable person slot.</param>
    [Theory]
    [InlineData((int)SupervisorSelectionKind.NthPerson, 0)]
    [InlineData((int)SupervisorSelectionKind.NthPerson, -1)]
    [InlineData((int)SupervisorSelectionKind.NthPerson, null)]
    [InlineData((int)SupervisorSelectionKind.All, 1)]
    [InlineData((int)SupervisorSelectionKind.RequireUnique, 1)]
    [InlineData((int)SupervisorSelectionKind.HighestBusinessPriority, 1)]
    [InlineData(99, null)]
    public void IgnoredOrInvalidPersonRankIsRejected(
        int kind,
        int? number
    )
    {
        // Arrange
        var selection = new SupervisorSelection((SupervisorSelectionKind)kind, number);

        // Act
        var error = Record.Exception(selection.Validate);

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>Every source owner can match a different catalog field on every exact candidate owner.</summary>
    /// <param name="source">The source owner supplying the routing scalar.</param>
    /// <param name="candidate">The candidate owner supplying its independently named routing scalar.</param>
    [Theory]
    [InlineData((int)MetadataOwner.SourceUser, (int)MetadataOwner.CandidateUser)]
    [InlineData((int)MetadataOwner.SourceUser, (int)MetadataOwner.CandidateMembership)]
    [InlineData((int)MetadataOwner.SourceUser, (int)MetadataOwner.CandidateGroup)]
    [InlineData((int)MetadataOwner.SourceMembership, (int)MetadataOwner.CandidateUser)]
    [InlineData((int)MetadataOwner.SourceMembership, (int)MetadataOwner.CandidateMembership)]
    [InlineData((int)MetadataOwner.SourceMembership, (int)MetadataOwner.CandidateGroup)]
    [InlineData((int)MetadataOwner.SourceGroup, (int)MetadataOwner.CandidateUser)]
    [InlineData((int)MetadataOwner.SourceGroup, (int)MetadataOwner.CandidateMembership)]
    [InlineData((int)MetadataOwner.SourceGroup, (int)MetadataOwner.CandidateGroup)]
    public void DifferentRoutingFieldsOnExactOwnersAreAccepted(
        int source,
        int candidate
    )
    {
        // Arrange
        var request = ValidRequest() with
        {
            Matches = [new MetadataMatch((MetadataOwner)source, "Route", (MetadataOwner)candidate, "ManagerRoute")],
        };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.Null(error);
    }

    /// <summary>
    /// Wrong owner direction, unknown fields, and null conditions cannot become dynamic SQL identifiers.
    /// </summary>
    /// <param name="invalid">The malformed owner, field, or condition supplied to validation.</param>
    [Theory]
    [InlineData("source-owner")]
    [InlineData("candidate-owner")]
    [InlineData("undefined-owner")]
    [InlineData("source-field")]
    [InlineData("candidate-field")]
    [InlineData("null-condition")]
    public void InvalidRoutingConditionIsRejected(string invalid)
    {
        // Arrange
        var match = new MetadataMatch(MetadataOwner.SourceUser, "Route", MetadataOwner.CandidateGroup, "Route");
        match = invalid switch
        {
            "source-owner" => match with { SourceOwner = MetadataOwner.CandidateUser },
            "candidate-owner" => match with { CandidateOwner = MetadataOwner.SourceGroup },
            "undefined-owner" => match with { CandidateOwner = (MetadataOwner)99 },
            "source-field" => match with { SourceFieldCode = "Unknown" },
            "candidate-field" => match with { CandidateFieldCode = "Route; DROP TABLE Users" },
            _ => null!,
        };

        var request = ValidRequest() with { Matches = [match] };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>An undefined search boundary cannot silently switch the inclusion of the source group.</summary>
    [Fact]
    public void UnknownSearchStartIsRejected()
    {
        // Arrange
        var request = ValidRequest() with { SearchStart = (SearchStart)99 };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(error);
    }

    /// <summary>A null match collection differs from explicitly choosing no metadata filter.</summary>
    [Fact]
    public void MissingMatchCollectionIsRejected()
    {
        // Arrange
        var request = ValidRequest() with { Matches = null! };

        // Act
        var error = Record.Exception(() => SupervisorRequestValidation.Validate(request));

        // Assert
        Assert.IsType<ArgumentNullException>(error);
    }

    /// <summary>Creates a request with explicit choices and no implicit date or metadata filter.</summary>
    private static SupervisorRequest ValidRequest() => new()
    {
        SourceUserId = 42,
        SearchStart = SearchStart.IncludeSelf,
        Roles = new NoRoleFilter(),
        Groups = new AllEligibleGroups(),
        Matches = [],
    };

    /// <summary>Constructs role alternatives for validation without database setup.</summary>
    /// <param name="role">The role alternative selected by the validation case.</param>
    /// <param name="id">The role identity used by identity-bearing alternatives.</param>
    private static SupervisorRoles Role(
        string role,
        Guid id
    ) => role switch
    {
        "none" => new NoRoleFilter(),
        "membership" => new MembershipRoleFilter(id),
        "global" => new GlobalUserRole(id),
        "either" => new EitherRole(id),
        "unknown" => new UnknownRole(),
        _ => null!,
    };

    /// <summary>Constructs group alternatives for validation without database setup.</summary>
    /// <param name="group">The group alternative selected by the validation case.</param>
    /// <param name="rank">The one-based rank used by ranked alternatives.</param>
    private static SupervisorGroups Groups(
        string group,
        int rank
    ) => group switch
    {
        "all" => new AllEligibleGroups(),
        "eligible" => new NthEligibleGroup([2, 4], rank),
        "type" => new NthTypeGroupThenValidate(2, rank),
        "null-types" => new AllEligibleGroups(null!),
        "null-eligible-types" => new NthEligibleGroup(null!, rank),
        "unknown" => new UnknownGroups(),
        _ => null!,
    };

    /// <summary>Ensures an unrecognized future alternative fails closed.</summary>
    private sealed record UnknownRole : SupervisorRoles;

    /// <summary>Ensures an unrecognized future group alternative fails closed.</summary>
    private sealed record UnknownGroups : SupervisorGroups;
}
