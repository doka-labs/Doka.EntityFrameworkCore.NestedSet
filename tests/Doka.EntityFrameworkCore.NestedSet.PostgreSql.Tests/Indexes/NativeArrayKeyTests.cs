namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Verifies PostgreSQL native array-key equality using an isolated fixture.</summary>
public sealed class NativeArrayKeyTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, PostgreSqlEngine>>
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the exact provider fixture owning this suite's database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public NativeArrayKeyTests(
        ProviderFixture<RelationalFixture, PostgreSqlEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// Provider-native array keys retain scalar parameter equality without jagged collection translation.
    /// </summary>
    [Fact]
    public async Task ArrayKeysUseNativeScalarEquality()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var source = database.CreateContext();
        var probe = new EnterpriseProbe(captureParameterBindings: true);
        var options = new DbContextOptionsBuilder<NativeArrayContext>()
            .ConfigureTestWarnings()
            .UseNpgsql(source.Database.GetConnectionString())
            .UseNestedSets()
            .AddInterceptors(probe)
            .Options;

        await using var context = new NativeArrayContext(options);
        _ = Mapping.NestedSetMapping<NativeArrayNode, int[], int>.For(
            context,
            context.Model.FindEntityType(typeof(NativeArrayNode))!);

        await context
            .GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        await context.AddRangeAsync(
            [
                new NativeArrayNode
                {
                    Id = [1, 2],
                    Scope = 1,
                    Left = 1,
                    Right = 2,
                },
                new NativeArrayNode
                {
                    Id = [3, 4],
                    Scope = 1,
                    Left = 3,
                    Right = 4,
                    Position = 1,
                },
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var keys = new[] { new[] { 1, 2 }, new[] { 9, 9 } };
        var commandIndex = probe.Commands.Count;
        var keyProperty =
            context.Model.FindEntityType(typeof(NativeArrayNode))!.FindProperty(nameof(NativeArrayNode.Id))!;

        // Act
        var found = await context
            .Set<NativeArrayNode>()
            .Where(Storage.NestedSetKeyFilter<NativeArrayNode>.Matches(keyProperty, keys))
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], Assert.Single(found));
        Assert.Equal(2, probe.ParameterNames[commandIndex].Length);
        Assert.DoesNotContain("ANY", probe.Commands[commandIndex], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Exercises an array-valued PostgreSQL primary key outside the scalar collection optimization.</summary>
    private sealed class NativeArrayContext : DbContext
    {
        /// <summary>Uses the fixture-owned PostgreSQL connection.</summary>
        internal NativeArrayContext(
            DbContextOptions<NativeArrayContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<NativeArrayNode>();

            node.ToTable("KeyFilter_Array");
            node
                .Property(entity => entity.Id)
                .ValueGeneratedNever();
            node.HasNestedSet(builder => builder
                .HasTreeId(entity => entity.TreeId)
                .HasScope(entity => entity.Scope)
                .HasParent(entity => entity.ParentId));
        }
    }

    /// <summary>Uses PostgreSQL's native integer-array equality for both primary and parent keys.</summary>
    private sealed class NativeArrayNode : IScopedNestedSetNode<int[], Guid, int>
    {
        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int[] Id { get; set; } = [];

        /// <summary>Gets or sets the scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets an optional native array parent key.</summary>
        public int[]? ParentId { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }
}
