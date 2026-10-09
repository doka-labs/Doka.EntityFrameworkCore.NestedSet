using Doka.EntityFrameworkCore.NestedSet.Tests.CompiledModels;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies exact detached refresh assignments through finalized EF metadata.</summary>
public sealed class DetachedRefreshValueTests
{
    /// <summary>Comparer-equal database values replace the actual CLR representation without tracker state.</summary>
    [Fact]
    public async Task ComparerEqualBinaryValueIsAssignedExactly()
    {
        // Arrange
        await using var context = CreateContext();
        var node = new RefreshNode { Token = [1] };
        byte[] databaseValue = [2];
        var property = context.Model.FindEntityType(typeof(RefreshNode))!.FindProperty(nameof(RefreshNode.Token))!;
        var setter = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        setter.SetClrValueUsingContainingEntity(node, databaseValue);

        // Assert
        Assert.Same(databaseValue, node.Token);
        Assert.Equal<byte>([2], node.Token);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Mapped backing fields receive the database value even when their public view is read-only.</summary>
    [Fact]
    public async Task FieldAccessUsesTheConfiguredBackingField()
    {
        // Arrange
        await using var context = CreateContext();
        var node = new RefreshNode();
        var property = context.Model.FindEntityType(typeof(RefreshNode))!.FindProperty("FieldRevision")!;
        var setter = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        setter.SetClrValueUsingContainingEntity(node, 29);

        // Assert
        Assert.Equal(29, node.FieldRevision);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A shared property bag receives its mapped indexer value using the exact named metadata.</summary>
    [Fact]
    public async Task SharedIndexerValueUsesItsConfiguredName()
    {
        // Arrange
        await using var context = CreateContext();
        var node = new Dictionary<string, object>
        {
            ["Id"] = 1,
            ["Revision"] = 0,
        };

        var property = context.Model.FindEntityType("RefreshBag")!.FindProperty("Revision")!;
        var setter = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        setter.SetClrValueUsingContainingEntity(node, 37);

        // Assert
        Assert.Equal(37, node["Revision"]);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Shadow values have no detached CLR storage and require no context entry.</summary>
    [Fact]
    public async Task ShadowValueDoesNotCreateADetachedEntry()
    {
        // Arrange
        await using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(RefreshNode))!.FindProperty("ShadowRevision")!;

        // Act
        var setter = NestedSetDetachedValueSetter.Get(property);

        // Assert
        Assert.Null(setter);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Nested value-complex leaves copy back through enclosing values while references stay stable.</summary>
    [Fact]
    public async Task NestedValueComplexAssignmentCopiesBackToTheContainingEntity()
    {
        // Arrange
        await using var context = CreateContext();
        var envelope = new RefreshEnvelope { Body = new RefreshBody(new RefreshDetails(3)) };
        var node = new RefreshNode { Envelope = envelope };
        var property =
            context.Model.FindEntityType(typeof(RefreshNode))!.FindComplexProperty(nameof(RefreshNode.Envelope))!
                .ComplexType.FindComplexProperty(nameof(RefreshEnvelope.Body))!.ComplexType
                .FindComplexProperty(nameof(RefreshBody.Details))!.ComplexType
                .FindProperty(nameof(RefreshDetails.Revision))!;

        var setter = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        setter.SetClrValueUsingContainingEntity(node, 41);

        // Assert
        Assert.Same(envelope, node.Envelope);
        Assert.Equal(41, node.Envelope.Body.Details.Revision);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Application setter failures retain their exact exception without a reflection wrapper.</summary>
    [Fact]
    public async Task ApplicationSetterFailureKeepsItsOriginalException()
    {
        // Arrange
        await using var context = CreateContext();
        var node = new RefreshNode();
        var property = context.Model.FindEntityType(typeof(RefreshNode))!.FindProperty(nameof(RefreshNode.Rejected))!;
        var setter = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        var error = Record.Exception(() => setter.SetClrValueUsingContainingEntity(node, 43));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("Rejected refresh value.", error.Message);
        Assert.Equal(0, node.Rejected);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Supplied finalized models reuse metadata setters without retaining the creating context.</summary>
    [Fact]
    public async Task SuppliedModelReusesTheSameSetter()
    {
        // Arrange
        await using var source = CreateContext();
        var model = source.Model;
        var options = new DbContextOptionsBuilder<RefreshContext>()
            .UseSqlite("Data Source=:memory:")
            .UseModel(model)
            .Options;

        await using var consumer = new RefreshContext(options);
        var property = model.FindEntityType(typeof(RefreshNode))!.FindProperty("FieldRevision")!;
        var first = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        var reused = NestedSetDetachedValueSetter.Get(
            consumer.Model.FindEntityType(typeof(RefreshNode))!.FindProperty("FieldRevision")!);

        // Assert
        Assert.Same(first, reused);
        Assert.Empty(source.ChangeTracker.Entries());
        Assert.Empty(consumer.ChangeTracker.Entries());
    }

    /// <summary>Generated runtime metadata supplies the same setter seam without rebuilding a design model.</summary>
    [Fact]
    public async Task GeneratedCompiledModelUsesItsMetadataSetter()
    {
        // Arrange
        var model = CompiledTreeContextModel.Instance;
        var options = new DbContextOptionsBuilder<CompiledTreeContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .UseModel(model)
            .Options;

        await using var context = new CompiledTreeContext(options, rejectModelBuilding: true);
        var node = new CompiledNumber();
        var property = context.Model.FindEntityType(typeof(CompiledNumber))!.FindProperty(nameof(CompiledNumber.Left))!;

        var setter = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        setter.SetClrValueUsingContainingEntity(node, 53L);

        // Assert
        Assert.Same(model, context.Model);
        Assert.Equal(53L, node.Left);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates a provider-backed finalized model without opening a database connection.</summary>
    private static RefreshContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RefreshContext>().UseSqlite("Data Source=:memory:").Options;

        return new RefreshContext(options);
    }

    /// <summary>Maps every CLR access shape used by the detached bulk-refresh setter.</summary>
    private sealed class RefreshContext : DbContext
    {
        /// <summary>Uses a finalized model supplied or built by the test.</summary>
        internal RefreshContext(
            DbContextOptions<RefreshContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<RefreshNode>();
            node
                .Property<int>("FieldRevision")
                .HasField("_fieldRevision")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            node.Property<int>("ShadowRevision");
            node
                .Property(value => value.Token)
                .Metadata
                .SetValueComparer(
                    new ValueComparer<byte[]>(
                        (
                            first,
                            second
                        ) => true,
                        value => 0,
                        value => value.ToArray()));

            node
                .Property(value => value.Rejected)
                .UsePropertyAccessMode(PropertyAccessMode.Property);

            var envelope = node.ComplexProperty(value => value.Envelope);
            var body = envelope.ComplexProperty(value => value.Body);
            body.UsePropertyAccessMode(PropertyAccessMode.Property);
            var details = body.ComplexProperty(value => value.Details);
            details.UsePropertyAccessMode(PropertyAccessMode.Property);
            details
                .Property(value => value.Revision)
                .UsePropertyAccessMode(PropertyAccessMode.Property);

            var bag = modelBuilder.SharedTypeEntity<Dictionary<string, object>>("RefreshBag");
            bag.IndexerProperty<int>("Id");
            bag.IndexerProperty<int>("Revision");
            bag.HasKey("Id");
        }
    }

    /// <summary>Provides scalar, field and complex storage without any state-manager requirements.</summary>
    private sealed class RefreshNode
    {
        private int _fieldRevision = -1;
        private int _rejected;

        /// <summary>Gets or sets the model identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets the configured field-backed generated value.</summary>
        public int FieldRevision => _fieldRevision;

        /// <summary>Gets or sets the binary representation tested with an intentionally broad comparer.</summary>
        public byte[] Token { get; set; } = [];

        /// <summary>Gets or sets the reference container enclosing two value-complex levels.</summary>
        public RefreshEnvelope Envelope { get; set; } = new();

        /// <summary>Rejects a nonzero database value to exercise the original application setter exception.</summary>
        public int Rejected
        {
            get => _rejected;
            set
            {
                if (value != 0)
                {
                    throw new InvalidOperationException("Rejected refresh value.");
                }

                _rejected = value;
            }
        }
    }

    /// <summary>Preserves reference identity while a nested complex scalar is refreshed.</summary>
    private sealed class RefreshEnvelope
    {
        /// <summary>Gets or sets the first value-complex level.</summary>
        public RefreshBody Body { get; set; }
    }

    /// <summary>Provides a value-complex parent requiring copyback after its child changes.</summary>
    /// <param name="Details">The nested value-complex container.</param>
    private readonly record struct RefreshBody(RefreshDetails Details)
    {
        /// <summary>Allows EF to materialize the container before assigning its complex child.</summary>
        public RefreshBody() : this(default) { }
    }

    /// <summary>Provides the generated scalar inside a nested value-complex leaf.</summary>
    /// <param name="Revision">The final database-generated value.</param>
    private readonly record struct RefreshDetails(int Revision);
}
