namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Coordinates ordinary asynchronous saves with configured nested-set sibling ordering.</summary>
/// <remarks>
///     Applications with an existing context base class can use
///     <see cref="NestedSetDbContextExtensions.SaveNestedSetChangesAsync" /> instead.
///     Synchronous saves reject changes that require hierarchy ordering; use an asynchronous save for those changes.
/// </remarks>
public abstract class NestedSetDbContext : DbContext
{
    /// <summary>Creates a context whose provider is configured by the derived context.</summary>
    protected NestedSetDbContext() { }

    /// <summary>Creates a context using the supplied EF Core options.</summary>
    /// <param name="options">The options passed to the underlying EF Core context.</param>
    protected NestedSetDbContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Saves tracked changes and restores sibling order before accepting changes when requested.</summary>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept tracked changes after the boundary succeeds.</param>
    /// <param name="cancellationToken">The token used for locking, saving, and structural repair.</param>
    /// <returns>The number of state entries written by EF Core, excluding structural SQL updates.</returns>
    /// <remarks>
    ///     The inherited token-only overload dispatches to this override with acceptance enabled.
    ///     With false, payload originals and modified flags retain EF Core's normal pending-save semantics.
    ///     Managed hierarchy coordinates are refreshed and accepted individually to prevent a later structural write.
    /// </remarks>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    ) => this.SaveNestedSetChangesAsync(
        acceptAllChangesOnSuccess,
        token => base.SaveChangesAsync(false, token),
        cancellationToken);

    /// <summary>Saves synchronously when hierarchy changes do not require asynchronous coordination.</summary>
    /// <returns>The number of state entries written by EF Core.</returns>
    /// <exception cref="InvalidOperationException">An ordered hierarchy requires asynchronous coordination.</exception>
    public override int SaveChanges() => SaveChanges(true);

    /// <summary>Saves synchronously only when configured hierarchy ordering does not need maintenance.</summary>
    /// <param name="acceptAllChangesOnSuccess">Whether EF Core accepts successfully saved changes.</param>
    /// <returns>The number of state entries written by EF Core.</returns>
    /// <exception cref="InvalidOperationException">An ordered hierarchy requires asynchronous coordination.</exception>
    public override int SaveChanges(
        bool acceptAllChangesOnSuccess
    ) =>
        // ReSharper disable once MethodHasAsyncOverload
        // WHY: This explicitly synchronous API preserves native EF acceptance while guarding post-callback state.
        this.SaveNestedSetChanges(acceptAllChangesOnSuccess, () => base.SaveChanges(false));
}
