using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks actual query workloads without a statistical runner or numeric budgets.</summary>
public sealed class QueryMeasurementContractTests
{
    /// <summary>Enumerates every retained lexical source shape and original depth.</summary>
    public static TheoryData<string, int> EndpointCases
    {
        get
        {
            var data = new TheoryData<string, int>();
            string[] shapes =
            [
                "dag", "identity", "projected", "alternating-same", "alternating-foreign", "object-payload",
            ];

            foreach (var shape in shapes)
            {
                foreach (var depth in new[] { 12, 16, 24, 48 })
                {
                    data.Add(shape, depth);
                }
            }

            return data;
        }
    }

    /// <summary>Fixed and explicit-array workload paths preserve each selected expression's identity.</summary>
    /// <param name="arity">The independently supplied binding count.</param>
    /// <param name="member">Whether the selected expression is an initializer assignment.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void FixedBindings_Workload_RetainsGeneralResult(
        int arity,
        bool member
    )
    {
        // Arrange
        var benchmark = new FixedBindingBenchmarks { Arity = arity, Member = member };
        benchmark.Setup();

        // Act
        var current = benchmark.Fixed();
        var general = benchmark.GeneralArray();

        // Assert
        Assert.Same(general, current);
        benchmark.Validate();
    }

    /// <summary>An unavailable fixed overload rejects setup before constructing observation inputs.</summary>
    /// <param name="arity">The unsupported fixed-overload arity.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void FixedBindings_UnsupportedArity_RejectsSetup(
        int arity
    )
    {
        // Arrange
        var benchmark = new FixedBindingBenchmarks { Arity = arity };

        // Act
        var error = Record.Exception(benchmark.Setup);

        // Assert
        Assert.Equal("arity", Assert.IsType<ArgumentOutOfRangeException>(error).ParamName);
    }

    /// <summary>Static and ordinary captured getter traps remain unevaluated in the actual workload.</summary>
    /// <param name="arity">The selected fixed overload.</param>
    /// <param name="staticMember">Whether the getter belongs to the type rather than its captured instance.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void UnboundMembers_Workload_RetainsDeferredGetter(
        int arity,
        bool staticMember
    )
    {
        // Arrange
        var benchmark = new UnboundMemberBenchmarks { Arity = arity, StaticMember = staticMember };
        benchmark.Setup();

        // Act
        var result = benchmark.Inline();

        // Assert
        Assert.NotNull(result);
        benchmark.Validate();
    }

    /// <summary>Unbound-member observations also require an existing fixed overload.</summary>
    /// <param name="arity">The unsupported fixed-overload arity.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void UnboundMembers_UnsupportedArity_RejectsSetup(
        int arity
    )
    {
        // Arrange
        var benchmark = new UnboundMemberBenchmarks { Arity = arity };

        // Act
        var error = Record.Exception(benchmark.Setup);

        // Assert
        Assert.Equal("arity", Assert.IsType<ArgumentOutOfRangeException>(error).ParamName);
    }

    /// <summary>The projection and replacement micro-workloads retain their lexical argument identities.</summary>
    [Fact]
    public void ProjectionWorkloads_RetainIndependentSemanticOracles()
    {
        // Arrange
        var member = new MemberInitializerLookupBenchmarks();
        var replacement = new ProjectionReplacementBenchmarks();
        member.Setup();
        replacement.Setup();

        // Act
        var selected = member.SelectMember();
        var direct = replacement.Replace();
        var synthetic = replacement.SyntheticLambda();

        // Assert
        Assert.NotNull(selected);
        Assert.Same(synthetic, direct);
        member.Validate();
        replacement.Validate();
    }

    /// <summary>The SQL workload preserves equivalent logical binding without opening a connection.</summary>
    [Fact]
    public void FusedBinding_Workload_RetainsEquivalentSql()
    {
        // Arrange
        var benchmark = new FusedBindingBenchmarks();
        benchmark.Setup();
        try
        {
            // Act
            var separate = benchmark.SeparatePredicates();
            var fused = benchmark.FusedPredicate();

            // Assert
            Assert.Equal(separate, fused);
            benchmark.Validate();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Mapped and direct member construction retain the same caller-owned alias.</summary>
    [Fact]
    public void MappedMember_Workload_RetainsMemberIdentity()
    {
        // Arrange
        var benchmark = new MappedMemberReadBenchmarks();
        benchmark.Setup();
        try
        {
            // Act
            var direct = Assert.IsType<MemberExpression>(benchmark.DirectMember(), exactMatch: false);
            var mapped = Assert.IsType<MemberExpression>(benchmark.MappedMember(), exactMatch: false);

            // Assert
            Assert.Same(direct.Expression, mapped.Expression);
            Assert.Same(direct.Member, mapped.Member);
            benchmark.Validate();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Shared-source workloads retain exact mapping or reject a deliberate foreign-mapping input.</summary>
    /// <param name="shape">The source composition variant.</param>
    /// <param name="depth">The statement-local shared-source depth.</param>
    [Theory]
    [MemberData(nameof(EndpointCases))]
    public void EndpointDag_Workload_RetainsExactOutcome(
        string shape,
        int depth
    )
    {
        // Arrange
        var benchmark = new EndpointDagBenchmarks { Shape = shape, Depth = depth };
        benchmark.Setup();
        try
        {
            // Act
            var visible = benchmark.Resolve();

            // Assert
            Assert.Equal(shape != "alternating-foreign", visible);
            benchmark.Validate();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>An unknown source observation cannot create an unowned provider during failed setup.</summary>
    [Fact]
    public void EndpointDag_UnknownShape_RejectsSetup()
    {
        // Arrange
        var benchmark = new EndpointDagBenchmarks { Shape = "unknown", Depth = 12 };

        // Act
        var error = Record.Exception(benchmark.Setup);

        // Assert
        Assert.Equal(nameof(EndpointDagBenchmarks.Shape), Assert.IsType<ArgumentOutOfRangeException>(error).ParamName);
    }

    /// <summary>Increasing references retains the actual proof result without a numeric assertion.</summary>
    /// <param name="references">The repeated immutable subtree count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    public void VisibilityProof_Workload_RetainsBoundSource(
        int references
    )
    {
        // Arrange
        var benchmark = new VisibilityProofBenchmarks { References = references };
        benchmark.Setup();
        try
        {
            // Act
            var visible = benchmark.Prove();

            // Assert
            Assert.True(visible);
            benchmark.Validate();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
