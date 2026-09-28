namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Retains native detection and acceptance when the model has no ordering configuration.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ModelWithoutOrderingUsesNativeDetection(
        bool synchronous,
        bool automaticDetection,
        bool acceptChanges
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        await using var context = new UnorderedSaveContext(new DbContextOptions<DbContext>(extensions));
        var marker = new OrderingMarker
        {
            Id = 1,
            Value = "before callback",
        };

        await context.AddAsync(marker, CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        context.SavingChanges += (_, _) => marker.Value = "callback payload";
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        // Act
        // ReSharper disable once MethodHasAsyncOverload
        // WHY: The synchronous cases exercise the explicitly synchronous wrapper's native detection policy.
        var saved = synchronous
            ? context.SaveChanges(acceptChanges)
            : await context.SaveChangesAsync(acceptChanges, CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal(automaticDetection ? 1 : 0, detections);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            acceptChanges ? EntityState.Unchanged : EntityState.Added,
            context.Entry(marker).State);
        Assert.Equal(
            "callback payload",
            await setup
                .Set<OrderingMarker>()
                .Select(value => value.Value)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Leaves an undetected callback edit pending when the application disables native detection.</summary>
    [Fact]
    public async Task UnorderedManualDetectionDoesNotPersistUndetectedCallbackChanges()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await setup.AddAsync(
            new OrderingMarker
            {
                Id = 1,
                Value = "original",
            },
            CancellationToken.None);

        await setup.SaveChangesAsync(CancellationToken.None);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        await using var context = new UnorderedSaveContext(new DbContextOptions<DbContext>(extensions));
        var marker = await context
            .Set<OrderingMarker>()
            .SingleAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        context.SavingChanges += (_, _) => marker.Value = "undetected callback";

        // Act
        var saved = await context.SaveChangesAsync(false, CancellationToken.None);

        // Assert
        Assert.Equal(0, saved);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal("undetected callback", marker.Value);
        Assert.Equal(
            "original",
            await setup
                .Set<OrderingMarker>()
                .Select(value => value.Value)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Retains native pending callback state and detection settings after an unordered failure.</summary>
    [Fact]
    public async Task UnorderedFailureRetainsNativeCallbackState()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var probe = new SaveBoundaryProbe { FailNextPayload = true };
        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .AddInterceptors(probe);

        await using var context = new UnorderedSaveContext(options.Options);
        var marker = new OrderingMarker
        {
            Id = 1,
            Value = "before callback",
        };

        await context.AddAsync(marker, CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        context.SavingChanges += (_, _) => marker.Value = "pending callback";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.NotNull(failure);
        Assert.Contains(nameof(SaveBoundaryTransientException), failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, probe.PayloadCommands);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(EntityState.Added, context.Entry(marker).State);
        Assert.Equal("pending callback", marker.Value);
        Assert.Equal(
            0,
            await setup
                .Set<OrderingMarker>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Uses the existing marker table without introducing ordered metadata into its EF model.</summary>
    private sealed class UnorderedSaveContext : NestedSetDbContext
    {
        /// <summary>Uses the fixture's provider and connection with its own immutable model.</summary>
        internal UnorderedSaveContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<OrderingMarker>()
                .ToTable("StrictOrderingMarkers");
            modelBuilder
                .Entity<OrderingMarker>()
                .Property(value => value.Id)
                .ValueGeneratedNever();
        }
    }
}
