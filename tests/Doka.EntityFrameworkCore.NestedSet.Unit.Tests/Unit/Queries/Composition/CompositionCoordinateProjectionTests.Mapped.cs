using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionCoordinateProjectionTests
{
    /// <summary>Mapped geometry removes calls while identity and generated shadow roles retain markers.</summary>
    /// <param name="fields">Whether the interval roles are field-only mappings.</param>
    /// <param name="includeNonLeaf">Whether the generated candidate flag participates.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MappedCoordinateGeometryAvoidsMethodCallsWithoutChangingSql(bool fields, bool includeNonLeaf)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<MappedCoordinateContext>()
            .UseSqlite(
                "Data Source=:memory:",
                provider => provider.UseNestedSets()).AddInterceptors(boundary, boundary.Commands);

        await using var services = CoordinateServices(options.Options);
        await using var context = new MappedCoordinateContext(options.UseInternalServiceProvider(services).Options);
        var mapping = fields ? MappedCoordinateContext.Fields : MappedCoordinateContext.Properties;
        var entity = context.Model.FindEntityType(mapping)!;
        var root = new NestedSetQuery<MappedCoordinateNode>(context, entity);
        var nodes = context.Set<MappedCoordinateNode>(mapping).AsQueryable();

        // Act
        var selected = CoordinateSelection(root, nodes, includeNonLeaf);
        var body = Assert.IsType<MemberInitExpression>(selected.Selector.Body);
        var bindings = body.Bindings.Cast<MemberAssignment>().ToArray();
        var geometry = bindings.Where(binding => binding.Member.Name is "Left" or "Right").ToArray();
        var baseline = OriginalGeometryMarkers(selected, entity, fields);
        var sql = selected.Query.ToQueryString();

        // Assert
        Assert.Equal(2, geometry.Length);
        Assert.All(geometry, binding =>
        {
            var member = Assert.IsAssignableFrom<MemberExpression>(binding.Expression);
            Assert.Same(selected.Selector.Parameters[0], member.Expression);
            Assert.Equal(fields ? MemberTypes.Field : MemberTypes.Property, member.Member.MemberType);
        });

        Assert.Collection(geometry,
            binding => Assert.Equal(fields ? "_start" : "Start",
                ((MemberExpression)binding.Expression).Member.Name),
            binding => Assert.Equal(fields ? "_end" : "End",
                ((MemberExpression)binding.Expression).Member.Name));
        var identity = bindings.Where(binding => binding.Member.Name is "Key" or "Tree" or "Scope").ToArray();
        Assert.Equal(3, identity.Length);
        Assert.All(identity, binding =>
        {
            var marker = Assert.IsAssignableFrom<MethodCallExpression>(binding.Expression);
            Assert.Equal(typeof(EF), marker.Method.DeclaringType);
            Assert.Equal(typeof(object), marker.Type);
            Assert.IsType<ConstantExpression>(marker.Arguments[1]);
        });

        Assert.Equal(includeNonLeaf ? 4 : 3, bindings.Count(binding => binding.Expression is MethodCallExpression));
        Assert.Contains("\"StoredStart\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"StoredEnd\"", sql, StringComparison.Ordinal);
        Assert.Equal(baseline.ToQueryString(), sql);
        Assert.Equal(typeof(string), entity.FindProperty("TreeId")!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Retains the original marker shape for independent exact SQL equivalence.</summary>
    private static IQueryable OriginalGeometryMarkers(
        (IQueryable Query, LambdaExpression Selector) selected,
        IEntityType entity,
        bool fields
    )
    {
        var body = (MemberInitExpression)selected.Selector.Body;
        var parameter = selected.Selector.Parameters[0];
        var bindings = body.Bindings.Cast<MemberAssignment>().Select(binding => binding.Member.Name switch
        {
            "Left" => Expression.Bind(binding.Member,
                NestedSetExpressions.Property(parameter, entity.FindProperty(fields ? "_start" : "Start")!)),
            "Right" => Expression.Bind(binding.Member,
                NestedSetExpressions.Property(parameter, entity.FindProperty(fields ? "_end" : "End")!)),
            _ => binding,
        });

        var selector = Expression.Lambda(selected.Selector.Type,
            Expression.MemberInit(body.NewExpression, bindings), selected.Selector.Parameters);

        var original = (MethodCallExpression)selected.Query.Expression;
        var query = original.Update(original.Object, [original.Arguments[0], Expression.Quote(selector)]);

        return selected.Query.Provider.CreateQuery(query);
    }

    /// <summary>Maps exact members and private fields independently on the same CLR type.</summary>
    private sealed class MappedCoordinateContext(DbContextOptions<MappedCoordinateContext> options) : DbContext(options)
    {
        /// <summary>Identifies the throwing-property mapping.</summary>
        internal const string Properties = "PropertyCoordinates";

        /// <summary>Identifies the private-field mapping.</summary>
        internal const string Fields = "FieldCoordinates";

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            Configure(modelBuilder, Properties, fields: false);
            Configure(modelBuilder, Fields, fields: true);
        }

        /// <summary>Keeps native converted identity separate from exact Int64 geometry access.</summary>
        private static void Configure(ModelBuilder modelBuilder, string mapping, bool fields)
        {
            modelBuilder.SharedTypeEntity<MappedCoordinateNode>(mapping, node =>
            {
                node.ToTable(mapping);
                node.HasKey(value => value.Id);
                node.Property(value => value.Id).ValueGeneratedNever();
                node.Property(value => value.TreeId).HasConversion<string>();
                node.Ignore(value => value.Left);
                var left = fields ? "_start" : nameof(MappedCoordinateNode.Start);
                var right = fields ? "_end" : nameof(MappedCoordinateNode.End);

                if (fields)
                {
                    node.Ignore(value => value.Start);
                    node.Ignore(value => value.End);
                }

                node.Property<long>(left).HasField("_start").HasColumnName("StoredStart")
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
                node.Property<long>(right).HasField("_end").HasColumnName("StoredEnd")
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
                node.Property<int>("Depth");
                node.Property<long>("Position");
                node.Property<bool>("HasChildren");
                node.HasNestedSet(hierarchy => hierarchy.HasNodeKey(value => value.Id)
                    .HasScope(value => value.Scope).HasTreeId(value => value.TreeId)
                    .HasParent(value => value.ParentId).HasBounds(left, right)
                    .HasDepth("Depth").HasPosition("Position").HasAncestorIndex("HasChildren"));
            });
        }
    }

    /// <summary>Rejects accidental evaluation of mapped and conventional geometry getters.</summary>
    private sealed class MappedCoordinateNode
    {
        private long _start = long.MinValue;
        private long _end = long.MinValue;

        /// <summary>Gets or sets the mapped node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets a converted native tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the nullable mapped parent.</summary>
        public int? ParentId { get; set; }

        /// <summary>Rejects lookup by an unmapped conventional role name.</summary>
        public long Left => throw new InvalidOperationException($"Unmapped geometry getter for {Id}.");

        /// <summary>Gets or sets the exact mapped interval start without permitting client reads.</summary>
        public long Start
        {
            get => throw new InvalidOperationException($"Mapped geometry getter for {_start}.");
            set => _start = value;
        }

        /// <summary>Gets or sets the exact mapped interval end without permitting client reads.</summary>
        public long End
        {
            get => throw new InvalidOperationException($"Mapped geometry getter for {_end}.");
            set => _end = value;
        }
    }
}
