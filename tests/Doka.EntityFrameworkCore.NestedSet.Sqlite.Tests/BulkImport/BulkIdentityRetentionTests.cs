using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies rejected import callbacks release native identities without clearing unrelated tracking.</summary>
[Collection("Allocation measurements")]
public sealed class BulkIdentityRetentionTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the fixture's exact SQLite engine selection.</summary>
    /// <param name="fixture">The provider fixture bound to this suite.</param>
    public BulkIdentityRetentionTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>Replacement of an accepted scalar key leaves no stale Find result after database rollback.</summary>
    /// <param name="generated">Whether EF generates the identity instead of accepting the caller's key.</param>
    /// <param name="afterSave">Whether the callback runs after acceptance instead of before persistence.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ScalarKeyRejectionReleasesInstalledIdentity(
        bool generated,
        bool afterSave
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        var installed = 0;

        if (afterSave)
        {
            context.SavedChanges += (_, _) => Mutate();
        }
        else
        {
            context.SavingChanges += (_, _) => Mutate();
        }

        // Act
        var result = await ImportScalarAsync(context, generated);

        // Assert
        Assert.True(result.Error is NestedSetException, result.Error?.ToString());
        Assert.True(installed > 0);
        Assert.True(result.Restored);
        Assert.Null(await context.FindAsync<ScalarNode>([installed], CancellationToken.None));
        Assert.Null(await context.FindAsync<ScalarNode>([99], CancellationToken.None));
        CollectInputs();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<ScalarNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
        return;

        void Mutate()
        {
            var entry = context
                .ChangeTracker
                .Entries<ScalarNode>()
                .Single();

            installed = (int)entry.Property(nameof(ScalarNode.Id)).CurrentValue!;
            entry.Entity.Id = 99;
        }
    }

    /// <summary>Mutable converted key or scope changes release root and owned identity maps after rejection.</summary>
    /// <param name="scope">Whether the callback mutates the composite scope instead of the node identity.</param>
    /// <param name="binary">Whether the mutable representation is a byte buffer instead of a converted class.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MutableKeyRejectionReleasesRootAndOwnedIdentities(
        bool scope,
        bool binary
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        context.SavedChanges += (_, _) =>
        {
            if (binary)
            {
                var node = context
                    .ChangeTracker
                    .Entries<BinaryNode>()
                    .Single()
                    .Entity;
                (scope ? node.Scope : node.Id)[0] = 4;
            }
            else
            {
                var node = context
                    .ChangeTracker
                    .Entries<MutableNode>()
                    .Single()
                    .Entity;
                (scope ? node.Scope : node.Id).Value = "changed";
            }
        };

        // Act
        var result = await ImportMutableAsync(context, binary);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(result.Error).Code);
        Assert.True(result.Restored);
        CollectInputs();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.False(result.Payload.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.Set<BinaryNode>().CountAsync(CancellationToken.None));
        Assert.Null(
            await context.FindAsync<MutableNode>(
                [new MutableIdentity("scope"), new MutableIdentity("node")],
                CancellationToken.None));

        Assert.Null(await context.FindAsync<BinaryNode>([new byte[] { 1 }, new byte[] { 3 }], CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Shallow EF binary snapshots do not cause a false cleanup failure for one rejected mutation.</summary>
    /// <param name="scope">Whether the callback changes the scope instead of the node identity.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShallowBinarySnapshotsReleaseRejectedIdentitiesWithoutDiscard(
        bool scope
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<ShallowBinaryContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new ShallowBinaryContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        context.SavedChanges += (_, _) =>
        {
            var input = context
                .ChangeTracker
                .Entries<BinaryNode>()
                .Single()
                .Entity;
            (scope ? input.Scope : input.Id)[0] = 4;
        };

        // Act
        var result = await ImportMutableAsync(context, binary: true);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(result.Error).Code);
        Assert.True(result.Restored);
        CollectInputs();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.False(result.Payload.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Null(await context.FindAsync<BinaryNode>([new byte[] { 1 }, new byte[] { 3 }], CancellationToken.None));
        Assert.Equal(0, await context.Set<BinaryNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>A successful import preserves mutable identities while releasing its native tracking.</summary>
    [Fact]
    public async Task SuccessfulImportReleasesMutableIdentities()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var node = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Payload = new IdentityPayload(),
        };

        // Act
        await context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(node))],
                CancellationToken.None);

        // Assert
        Assert.Equal("node", node.Id.Value);
        Assert.Equal("scope", node.Scope.Value);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>A subsequently attached imported aggregate resolves to the same native identity.</summary>
    [Fact]
    public async Task NativeFindResolvesTheAttachedImportedMutableIdentity()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var node = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Payload = new IdentityPayload(),
        };

        await context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(node))],
                CancellationToken.None);

        context.Attach(node);

        // Act
        var found = await context.FindAsync<MutableNode>(
            [new MutableIdentity("scope"), new MutableIdentity("node")],
            CancellationToken.None);

        // Assert
        Assert.Same(node, found);
        Assert.Equal(EntityState.Unchanged, context.Entry(node).State);
        Assert.Equal(1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>A rejected mutable principal key preserves its exact owned payload for a subsequent import.</summary>
    /// <param name="scope">Whether the callback changes the composite scope instead of the node key.</param>
    /// <param name="binary">Whether the representation is a binary buffer instead of a converted class.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MutableKeyRejectionPreservesOwnedPayloadForRetry(
        bool scope,
        bool binary
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var payload = new IdentityPayload { Label = "kept" };
        var bufferNode = new BinaryNode
        {
            Id = [3],
            Scope = [8],
            Payload = payload,
        };

        var classNode = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Payload = payload,
        };

        var mutated = false;
        context.SavedChanges += (_, _) =>
        {
            if (mutated)
            {
                return;
            }

            mutated = true;

            if (binary)
            {
                var input = context
                    .ChangeTracker
                    .Entries<BinaryNode>()
                    .Single()
                    .Entity;
                (scope ? input.Scope : input.Id)[0] = 4;
            }
            else
            {
                var input = context
                    .ChangeTracker
                    .Entries<MutableNode>()
                    .Single()
                    .Entity;
                (scope ? input.Scope : input.Id).Value = "changed";
            }
        };

        var rejection = await Record.ExceptionAsync(Import);

        // Act
        await Import();

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(rejection).Code);
        Assert.Same(payload, binary ? bufferNode.Payload : classNode.Payload);
        Assert.Equal("kept", payload.Label);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(binary ? 1 : 0, await context.Set<BinaryNode>().CountAsync(CancellationToken.None));
        Assert.Equal(binary ? 0 : 1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
        return;

        Task Import() => binary
            ? context
                .NestedSet<BinaryNode>()
                .ForScope<byte[]>([1])
                .InsertForestAsync(
                    [
                        new NestedSetTreeImport<BinaryNode, Guid>(
                            Guid.Empty,
                            new NestedSetBranch<BinaryNode>(bufferNode))
                    ],
                    CancellationToken.None)
            : context
                .NestedSet<MutableNode>()
                .ForScope(new MutableIdentity("scope"))
                .InsertForestAsync(
                    [
                        new NestedSetTreeImport<MutableNode, Guid>(
                            Guid.Empty,
                            new NestedSetBranch<MutableNode>(classNode))
                    ],
                    CancellationToken.None);
    }

    /// <summary>A callback that corrupts EF's mutable hash key receives an explicit context-disposal error.</summary>
    /// <param name="scope">Whether the callback corrupts the scope instead of the node identity.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedInPlaceIdentityMutationRequiresDiscardingTheContext(
        bool scope
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        context.SavingChanges += (_, _) =>
        {
            var node = context
                .ChangeTracker
                .Entries<MutableNode>()
                .Single()
                .Entity;

            var identity = scope ? node.Scope : node.Id;
            identity.Value = "intermediate";
            context.ChangeTracker.DetectChanges();
            identity.Value = "changed";
        };

        // Act
        var result = await ImportMutableAsync(context, binary: false);

        // Assert
        var errors = Assert
            .IsType<AggregateException>(result.Error)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(errors, error => error is NestedSetException { Code: NestedSetErrorCode.InvalidImport });
        Assert.Contains(
            errors,
            error => error is InvalidOperationException
                && error.Message.Contains("Discard the context", StringComparison.Ordinal));

        Assert.True(result.Restored);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Comparer-equal replacements around explicit detection preserve a valid mutable-key insertion.</summary>
    /// <param name="scope">Whether replacements affect the configured scope instead of the node key.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentIdentityReplacementsRemainValidAroundExplicitDetection(
        bool scope
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var node = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Payload = new IdentityPayload(),
        };

        context.SavingChanges += (_, _) =>
        {
            var input = context
                .ChangeTracker
                .Entries<MutableNode>()
                .Single()
                .Entity;

            if (scope)
            {
                input.Scope = new MutableIdentity("scope");
            }
            else
            {
                input.Id = new MutableIdentity("node");
            }

            context.ChangeTracker.DetectChanges();

            if (scope)
            {
                input.Scope = new MutableIdentity("scope");
            }
            else
            {
                input.Id = new MutableIdentity("node");
            }
        };

        // Act
        await context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(node))],
                CancellationToken.None);

        // Assert
        Assert.Equal("node", node.Id.Value);
        Assert.Equal("scope", node.Scope.Value);
        Assert.Equal(1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Later graph callbacks cannot strand an earlier scalar identity or its intermediate key.</summary>
    /// <param name="intermediate">Whether detection separates two replacements before the callback throws.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterTrackingMutationReleasesEveryIntroducedIdentity(
        bool intermediate
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        context.ChangeTracker.Tracking += (_, args) =>
        {
            if (args.Entry.Entity is not ScalarNode { Id: 2 }
                || args.State != EntityState.Added)
            {
                return;
            }

            var first = context
                .ChangeTracker
                .Entries<ScalarNode>()
                .Single(entry => entry.Entity.Id == 1)
                .Entity;

            first.Id = 99;

            if (intermediate)
            {
                context.ChangeTracker.DetectChanges();
                first.Id = 100;

                throw new InvalidOperationException("tracking-intermediate");
            }
        };

        // Act
        var result = await ImportTwoRootsAsync(context);

        // Assert
        if (intermediate)
        {
            Assert.Equal(
                "tracking-intermediate",
                Assert.IsType<InvalidOperationException>(result.Error).Message);
        }
        else
        {
            Assert.Equal(
                NestedSetErrorCode.InvalidImport,
                Assert.IsType<NestedSetException>(result.Error).Code);
        }

        Assert.True(result.Restored);
        CollectInputs();
        Assert.False(result.First.TryGetTarget(out _));
        Assert.False(result.Second.TryGetTarget(out _));
        Assert.Null(await context.FindAsync<ScalarNode>([1], CancellationToken.None));
        Assert.Null(await context.FindAsync<ScalarNode>([99], CancellationToken.None));
        Assert.Null(await context.FindAsync<ScalarNode>([100], CancellationToken.None));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<ScalarNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Ends both input frames before native retention is observed by the assertion frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<ScalarNode> First, WeakReference<ScalarNode> Second,
        Exception? Error, bool Restored)> ImportTwoRootsAsync(
        IdentityContext context
    )
    {
        var first = new ScalarNode
        {
            Id = 1,
            Left = 71,
            Right = 72,
        };

        var second = new ScalarNode
        {
            Id = 2,
            Left = 81,
            Right = 82,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<ScalarNode>()
            .InsertForestAsync(
                [
                    new NestedSetTreeImport<ScalarNode, Guid>(Guid.Empty, new NestedSetBranch<ScalarNode>(first)),
                    new NestedSetTreeImport<ScalarNode, Guid>(Guid.NewGuid(), new NestedSetBranch<ScalarNode>(second)),
                ],
                CancellationToken.None));

        return (new WeakReference<ScalarNode>(first), new WeakReference<ScalarNode>(second), error,
            first.Id == 1 && second.Id == 2 && first is { Left: 71, Right: 72 } && second is { Left: 81, Right: 82 });
    }

    /// <summary>Contains failed input references in a completed frame instead of the assertion frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<ScalarNode> Input, Exception? Error, bool Restored)> ImportScalarAsync(
        IdentityContext context,
        bool generated
    )
    {
        var node = new ScalarNode
        {
            Id = generated ? 0 : 3,
            Left = 71,
            Right = 72,
        };

        var error = await Record.ExceptionAsync(() => context
            .NestedSet<ScalarNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<ScalarNode, Guid>(Guid.Empty, new NestedSetBranch<ScalarNode>(node))],
                CancellationToken.None));

        return (new WeakReference<ScalarNode>(node), error,
            node.Id == (generated ? 0 : 3) && node is { Left: 71, Right: 72 });
    }

    /// <summary>Uses the configured mutable representation while keeping weak references outside JIT roots.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<object> Input, WeakReference<IdentityPayload> Payload,
        Exception? Error, bool Restored)> ImportMutableAsync(
        DbContext context,
        bool binary
    )
    {
        var payload = new IdentityPayload();

        if (binary)
        {
            var node = new BinaryNode
            {
                Id = [3],
                Scope = [8],
                Left = 71,
                Right = 72,
                Payload = payload,
            };

            var error = await Record.ExceptionAsync(() => context
                .NestedSet<BinaryNode>()
                .ForScope<byte[]>([1])
                .InsertForestAsync(
                    [new NestedSetTreeImport<BinaryNode, Guid>(Guid.Empty, new NestedSetBranch<BinaryNode>(node))],
                    CancellationToken.None));

            return (new WeakReference<object>(node), new WeakReference<IdentityPayload>(payload), error,
                node.Id.SequenceEqual(new byte[] { 3 })
                && node.Scope.SequenceEqual(new byte[] { 8 })
                && node is { Left: 71, Right: 72 });
        }

        var mutable = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Scope = new MutableIdentity("original"),
            Left = 71,
            Right = 72,
            Payload = payload,
        };

        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(mutable))],
                CancellationToken.None));

        return (new WeakReference<object>(mutable), new WeakReference<IdentityPayload>(payload), failure,
            mutable.Id.Value == "node" && mutable.Scope.Value == "original" && mutable is { Left: 71, Right: 72 });
    }

    /// <summary>Collects only after the no-inlining import frame has relinquished its caller-owned inputs.</summary>
    private static void CollectInputs()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Maps explicit shallow binary snapshots while keeping structural comparer semantics.</summary>
    private sealed class ShallowBinaryContext(DbContextOptions<ShallowBinaryContext> options)
        : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            var binary = builder.Entity<BinaryNode>();
            binary.ToTable("ShallowIdentityImportNodes");
            binary.HasKey(node => new
            {
                node.Scope,
                node.Id,
            });

            binary
                .Property(node => node.Id)
                .ValueGeneratedNever();

            foreach (var property in new[] { nameof(BinaryNode.Id), nameof(BinaryNode.Scope) })
            {
                binary
                    .Property<byte[]>(property)
                    .Metadata
                    .SetValueComparer(
                        new ValueComparer<byte[]>(
                            (left, right) => left != null && right != null && ((IEnumerable<byte>)left).SequenceEqual(right),
                            value => value.Length == 0 ? 0 : value[0],
                            value => value));
            }

            binary.HasNestedSet(set => set
                .HasNodeKey(node => node.Id)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Scope)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
            binary.OwnsOne(node => node.Payload);
            builder
                .Entity<IdentityMarker>()
                .ToTable("ShallowIdentityImportMarkers");
        }
    }

    /// <summary>Maps generated, binary and converted-class identities with relational owned dependents.</summary>
    private sealed class IdentityContext(DbContextOptions<IdentityContext> options) : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            builder
                .Entity<ScalarNode>()
                .ToTable("ImportIdentityScalarNodes");
            builder
                .Entity<ScalarNode>()
                .HasNestedSet(set => set
                    .HasTreeId(node => node.TreeId)
                    .HasParent(node => node.ParentId)
                    .HasBounds(node => node.Left, node => node.Right)
                    .HasDepth(node => node.Depth)
                    .HasPosition(node => node.Position));

            var binary = builder.Entity<BinaryNode>();
            binary.ToTable("ImportIdentityBinaryNodes");
            binary.HasKey(node => new
            {
                node.Scope,
                node.Id,
            });

            binary
                .Property(node => node.Id)
                .ValueGeneratedNever();
            binary.HasNestedSet(set => set
                .HasNodeKey(node => node.Id)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Scope)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
            binary.OwnsOne(node => node.Payload);

            var mutable = builder.Entity<MutableNode>();
            mutable.ToTable("ImportIdentityMutableNodes");

            foreach (var name in new[]
                     {
                         nameof(MutableNode.Id), nameof(MutableNode.Scope), nameof(MutableNode.ParentId),
                     })
            {
                var property = mutable.Property<MutableIdentity>(name);
                property
                    .HasMaxLength(40)
                    .HasConversion(value => value.Value, value => new MutableIdentity(value));
                property.Metadata.SetValueComparer(
                    new ValueComparer<MutableIdentity>(
                        (left, right) => left != null && right != null && left.Value == right.Value,
                        value => StringComparer.Ordinal.GetHashCode(value.Value),
                        value => new MutableIdentity(value.Value)));
            }

            mutable.HasKey(node => new
            {
                node.Scope,
                node.Id,
            });

            mutable
                .Property(node => node.Id)
                .ValueGeneratedNever();
            mutable.HasNestedSet(set => set
                .HasNodeKey(node => node.Id)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Scope)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
            mutable.OwnsOne(node => node.Payload);
            builder
                .Entity<IdentityMarker>()
                .ToTable("ImportIdentityMarkers");
        }
    }

    /// <summary>Provides an independently cloned mutable converted key representation.</summary>
    private sealed class MutableIdentity(string value)
    {
        /// <summary>Gets or sets the observable mutable identity representation.</summary>
        public string Value { get; set; } = value;
    }

    /// <summary>Provides a conventional generated scalar identity and unscoped hierarchy.</summary>
    private sealed class ScalarNode
    {
        /// <summary>Gets or sets the assigned or database-generated node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the tree root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Provides composite binary keys whose live buffers must not alias EF identity-map keys.</summary>
    private sealed class BinaryNode
    {
        /// <summary>Gets or sets the mutable binary node identity.</summary>
        public byte[] Id { get; set; } = [];

        /// <summary>Gets or sets the mutable binary scope identity.</summary>
        public byte[] Scope { get; set; } = [];

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional binary parent identity.</summary>
        public byte[]? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the tree root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the owned dependent whose native keys share principal values.</summary>
        public IdentityPayload Payload { get; set; } = new();
    }

    /// <summary>Provides independently mutable principal and composite scope identities.</summary>
    private sealed class MutableNode
    {
        /// <summary>Gets or sets the converted mutable node identity.</summary>
        public MutableIdentity Id { get; set; } = new(string.Empty);

        /// <summary>Gets or sets the converted mutable scope identity.</summary>
        public MutableIdentity Scope { get; set; } = new(string.Empty);

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional converted parent identity.</summary>
        public MutableIdentity? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the tree root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the owned dependent whose native keys share principal values.</summary>
        public IdentityPayload Payload { get; set; } = new();
    }

    /// <summary>Provides an owned composite-key dependent sharing its principal's identity values.</summary>
    private sealed class IdentityPayload
    {
        /// <summary>Gets or sets the payload preserved for a later insertion retry.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Provides unrelated application tracking that cleanup must preserve.</summary>
    private sealed class IdentityMarker
    {
        /// <summary>Gets or sets the independent application identity.</summary>
        public int Id { get; set; }
    }
}
