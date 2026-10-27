using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks mapped query reads against SQLite rather than the shape of their expression nodes.</summary>
public sealed class CompositionMappedMemberTests
{
    /// <summary>Every storage shape remains deferred and selects the exact mapped scalar without CLR getters.</summary>
    /// <param name="storage">The independently mapped property, field, shadow, indexer, or complex storage shape.</param>
    /// <param name="matches">Whether the server-side predicate should retain the stored boundary.</param>
    /// <returns>A task completing after a single asynchronous scalar query is checked.</returns>
    [Theory]
    [InlineData("property", true)]
    [InlineData("property", false)]
    [InlineData("field", true)]
    [InlineData("field", false)]
    [InlineData("backing-field", true)]
    [InlineData("backing-field", false)]
    [InlineData("shadow", true)]
    [InlineData("shadow", false)]
    [InlineData("indexer", true)]
    [InlineData("indexer", false)]
    [InlineData("complex", true)]
    [InlineData("complex", false)]
    public async Task MappedScalarReadsStayDeferredAndNeverInvokeClrGetters(
        string storage,
        bool matches
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var boundary = new ReaderBoundary();
        var options = new DbContextOptionsBuilder<MappedReadContext>()
            .UseSqlite(connection)
            .AddInterceptors(boundary)
            .Options;

        await using var context = new MappedReadContext(options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        // WHY: SQL fixture writes bypass the deliberately throwing application getters. The observed
        // query returns scalars, so a getter invocation identifies client evaluation or incorrect binding.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO PropertyNodes (Id, Left) VALUES (1, 7), (2, 13);
            INSERT INTO FieldNodes (Id, Left) VALUES (1, 7), (2, 13);
            INSERT INTO BackedNodes (Id, Left) VALUES (1, 7), (2, 13);
            INSERT INTO ShadowNodes (Id, StoredLeft) VALUES (1, 7), (2, 13);
            INSERT INTO BagNodes (Id, Left) VALUES (1, 7), (2, 13);
            INSERT INTO ComplexNodes (Id, Left) VALUES (1, 7), (2, 13);
            """, CancellationToken.None);

        boundary.Observe = true;
        var expected = matches ? 7L : 99L;

        // Act
        var query = SelectBoundary(context, storage).Where(value => value == expected);
        var sql = query.ToQueryString();
        var commandsBeforeMaterialization = boundary.CreatedCommands;
        var values = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(sql);
        Assert.Equal(0, commandsBeforeMaterialization);
        Assert.Equal(1, boundary.CreatedCommands);
        Assert.Equal(1, boundary.AsyncReaders);
        Assert.Equal(matches ? [7L] : Array.Empty<long>(), values);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Selects finalized metadata independently of conventional role and physical column names.</summary>
    private static IQueryable<long> SelectBoundary(
        MappedReadContext context,
        string storage
    ) => storage switch
    {
        "property" => Select(context.Set<PropertyNode>(),
            context.Model.FindEntityType(typeof(PropertyNode))!.FindProperty(nameof(PropertyNode.MappedLeft))!),
        "field" => Select(context.Set<FieldNode>(),
            context.Model.FindEntityType(typeof(FieldNode))!.FindProperty(nameof(FieldNode.StoredLeft))!),
        "backing-field" => Select(context.Set<BackedNode>(),
            context.Model.FindEntityType(typeof(BackedNode))!.FindProperty(nameof(BackedNode.MappedValue))!),
        "shadow" => Select(context.Set<ShadowNode>(),
            context.Model.FindEntityType(typeof(ShadowNode))!.FindProperty("StoredLeft")!),
        "indexer" => Select(context.Set<Dictionary<string, object>>("BagNodes"),
            context.Model.FindEntityType("BagNodes")!.FindProperty("Left")!),
        "complex" => Select(context.Set<ComplexNode>(),
            context.Model.FindEntityType(typeof(ComplexNode))!
                .FindComplexProperty(nameof(ComplexNode.Coordinates))!.ComplexType
                .FindProperty(nameof(Coordinates.Left))!),
        _ => throw new ArgumentOutOfRangeException(nameof(storage)),
    };

    /// <summary>Exercises the production mapped-read primitive inside an actual provider scalar projection.</summary>
    private static IQueryable<long> Select<TEntity>(
        IQueryable<TEntity> source,
        IProperty property
    ) where TEntity : class
    {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var selector = Expression.Lambda<Func<TEntity, long>>(
            NestedSetExpressions.QueryProperty(entity, property), entity);

        return source.Select(selector);
    }

    /// <summary>Keeps all storage shapes in one model while deliberately separating CLR and mapped names.</summary>
    private sealed class MappedReadContext(
        DbContextOptions<MappedReadContext> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var property = modelBuilder.Entity<PropertyNode>();
            property.ToTable("PropertyNodes");
            property.Ignore(node => node.Left);
            property.Property(node => node.MappedLeft)
                .HasField("_mappedLeft")
                .HasColumnName("Left")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            var field = modelBuilder.Entity<FieldNode>();
            field.ToTable("FieldNodes");
            field.Ignore(node => node.Left);
            field.Property<long>(nameof(FieldNode.StoredLeft))
                .HasField(nameof(FieldNode.StoredLeft))
                .HasColumnName("Left");

            var backed = modelBuilder.Entity<BackedNode>();
            backed.ToTable("BackedNodes");
            backed.Ignore(node => node.Left);
            // WHY: This valid property-backed mapping has a differently named backing field. Query
            // binding must retain the mapped PropertyInfo rather than substitute that field's CLR name.
            backed.Property(node => node.MappedValue)
                .HasField("_value")
                .HasColumnName("Left")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            var shadow = modelBuilder.Entity<ShadowNode>();
            shadow.ToTable("ShadowNodes");
            shadow.Ignore(node => node.Left);
            shadow.Property<long>("StoredLeft");

            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("BagNodes", bag =>
            {
                bag.ToTable("BagNodes");
                bag.IndexerProperty<int>("Id");
                bag.HasKey("Id");
                bag.IndexerProperty<long>("Left");
            });

            var complex = modelBuilder.Entity<ComplexNode>();
            complex.ToTable("ComplexNodes");
            complex.Ignore(node => node.Left);
            complex.ComplexProperty(node => node.Coordinates)
                .Property(coordinates => coordinates.Left)
                .HasField("_left")
                .HasColumnName("Left")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }

    /// <summary>Proves that a mapped property is translated without invoking its application getter.</summary>
    private sealed class PropertyNode
    {
        private long _mappedLeft;

        /// <summary>Gets or sets the ordinary row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the actual mapped member whose physical column has a different name.</summary>
        public long MappedLeft
        {
            get => throw new InvalidOperationException($"The mapped CLR getter must not run: {_mappedLeft}.");
            set => _mappedLeft = value;
        }

        /// <summary>Rejects accidental lookup by conventional role name instead of the mapped member.</summary>
        public long Left => throw new InvalidOperationException($"The unmapped role-name getter must not run: {Id}.");
    }

    /// <summary>Provides field-only scalar storage alongside an intentionally unusable role-name property.</summary>
    private sealed class FieldNode
    {
        /// <summary>Stores the mapped boundary without a CLR property.</summary>
        // WHY: A different CLR sentinel makes the fixture's SQL values explicit without a default-field
        // initialization that analyzers correctly flag; these scalar queries never construct a CLR row.
        public long StoredLeft = long.MinValue;

        /// <summary>Gets or sets the ordinary row identity.</summary>
        public int Id { get; set; }

        /// <summary>Rejects property-name guessing for the mapped field.</summary>
        public long Left => throw new InvalidOperationException($"The field-only role-name getter must not run: {Id}.");
    }

    /// <summary>Stores its boundary exclusively in EF shadow state.</summary>
    private sealed class ShadowNode
    {
        /// <summary>Gets or sets the ordinary row identity.</summary>
        public int Id { get; set; }

        /// <summary>Rejects substitution of the unmapped conventional role for shadow metadata.</summary>
        public long Left => throw new InvalidOperationException($"The shadow role-name getter must not run: {Id}.");
    }

    /// <summary>Maps a CLR property whose backing field and physical column both have different names.</summary>
    private sealed class BackedNode
    {
        private long _value;

        /// <summary>Gets or sets the ordinary row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the mapped scalar with an independently named backing field.</summary>
        public long MappedValue
        {
            get => throw new InvalidOperationException($"The property-backed getter must not run: {_value}.");
            set => _value = value;
        }

        /// <summary>Rejects conventional role-name lookup in place of the mapped property.</summary>
        public long Left => throw new InvalidOperationException($"The backed role-name getter must not run: {Id}.");
    }

    /// <summary>Places a scalar inside a complex mapping rather than directly on the entity.</summary>
    private sealed class ComplexNode
    {
        /// <summary>Gets or sets the ordinary row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the containing mapped complex value.</summary>
        public Coordinates Coordinates { get; set; } = new();

        /// <summary>Rejects binding the complex leaf to an unrelated root member with the same name.</summary>
        public long Left => throw new InvalidOperationException($"The root role-name getter must not run: {Id}.");
    }

    /// <summary>Provides an exact complex leaf that can only be read through SQL in these tests.</summary>
    private sealed class Coordinates
    {
        private long _left;

        /// <summary>Gets or sets the complex boundary through EF field access.</summary>
        public long Left
        {
            get => throw new InvalidOperationException($"The complex CLR getter must not run: {_left}.");
            set => _left = value;
        }
    }

    /// <summary>Rejects synchronous execution and records asynchronous readers after fixture setup.</summary>
    private sealed class ReaderBoundary : DbCommandInterceptor
    {
        /// <summary>Gets or sets whether execution belongs to the observed query rather than fixture setup.</summary>
        internal bool Observe { get; set; }

        /// <summary>Gets the number of observed asynchronous reader commands.</summary>
        internal int AsyncReaders { get; private set; }

        /// <summary>Gets all command creations, including non-reader commands, after fixture setup.</summary>
        internal int CreatedCommands { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult<DbCommand> CommandCreating(
            CommandCorrelatedEventData eventData,
            InterceptionResult<DbCommand> result
        )
        {
            if (Observe)
            {
                CreatedCommands++;
            }

            return result;
        }

        /// <inheritdoc />
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            if (Observe)
            {
                throw new InvalidOperationException("Mapped query reads must execute asynchronously.");
            }

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Observe)
            {
                AsyncReaders++;
            }

            return ValueTask.FromResult(result);
        }
    }
}
