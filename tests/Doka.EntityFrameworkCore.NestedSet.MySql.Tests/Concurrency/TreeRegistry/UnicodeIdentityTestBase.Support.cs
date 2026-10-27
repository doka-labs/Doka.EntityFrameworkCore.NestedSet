using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class UnicodeIdentityTestBase
{
    /// <summary>
    /// Runs one independent public mutation and coordinated-save scenario against exact native lengths.
    /// </summary>
    private async Task RoundTripAsync<TContext, TIdentity>(
        Func<DbContextOptions<TContext>, TContext> create,
        Func<string, TIdentity> identity,
        string role
    )
        where TContext : UnicodeIdentityContext<TIdentity>
        where TIdentity : notnull
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine, create);
        var firstId = Interlocked.Add(ref s_nextNode, 3);
        var binary = typeof(TContext) == typeof(UnicodeBinaryIdentityContext);
        var exact = binary ? string.Concat(Enumerable.Repeat("\U0001F600", 12)) : Supplementary('a');
        var neighbor = (binary ? "\U0001F600" : "a")
            + string.Concat(Enumerable.Repeat("\U0001F600", 10))
            + "\U0001F601";

        var ordinary = firstId.ToString("x8", CultureInfo.InvariantCulture);
        var scope = identity(role == "Scope" ? exact : ordinary);
        var treeId = identity(role == "TreeId" ? exact : ordinary);
        var otherScope = identity(role == "Scope" ? neighbor : ordinary);
        var otherTreeId = identity(role == "TreeId" ? neighbor : ordinary);
        var root = new UnicodeIdentityNode<TIdentity> { Id = firstId };
        var child = new UnicodeIdentityNode<TIdentity> { Id = firstId + 1 };
        var other = new UnicodeIdentityNode<TIdentity> { Id = firstId + 2 };
        var hierarchy = context.NestedSet<UnicodeIdentityNode<TIdentity>>();

        // Act
        await hierarchy
            .ForScope(scope)
            .InsertRootAsync(root, treeId, CancellationToken.None);

        await hierarchy
            .ForScope(scope)
            .InsertChildAsync(child, root.Id, CancellationToken.None);

        await hierarchy
            .ForScope(otherScope)
            .InsertRootAsync(other, otherTreeId, CancellationToken.None);

        // WHY: A facade is allowed to detach completed inserts. Loading the exact root proves that this save
        // contains a tracked ordering change and therefore constructs its own complete Unicode lock request.
        var trackedRoot = await context
            .Set<UnicodeIdentityNode<TIdentity>>()
            .SingleAsync(node => node.Id == root.Id, CancellationToken.None);

        trackedRoot.Name = "Updated";
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var rows = await hierarchy
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);
        var otherRows = await hierarchy
            .ForScope(otherScope)
            .InTree(otherTreeId)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(new[] { root.Id, child.Id }, rows.Select(node => node.Id));
        Assert.Equal(other.Id, Assert.Single(otherRows).Id);
        Assert.All(rows, node =>
        {
            Assert.Equal(scope, node.Scope);
            Assert.Equal(treeId, node.TreeId);
        });

        Assert.Equal("Updated", rows[0].Name);
        Assert.Equal(root.Id, rows[1].ParentId);
        Assert.Equal((1L, 4L, 0, 0L), (rows[0].Left, rows[0].Right, rows[0].Depth, rows[0].Position));
        Assert.Equal((2L, 3L, 1, 0L), (rows[1].Left, rows[1].Right, rows[1].Depth, rows[1].Position));
        Assert.True((await hierarchy.ForScope(scope).InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        var native = await ReadNativeAsync(context, root.Id, true);
        var nativeOther = await ReadNativeAsync(context, other.Id, true);
        Assert.Equal(role == "Scope" ? exact : ordinary, native.Scope);
        Assert.Equal(role == "TreeId" ? exact : ordinary, native.TreeId);
        Assert.Equal(role == "Scope" ? neighbor : ordinary, nativeOther.Scope);
        Assert.Equal(role == "TreeId" ? neighbor : ordinary, nativeOther.TreeId);
        Assert.Equal(12, role == "Scope" ? native.ScopeCharacters : native.TreeCharacters);
        Assert.Equal(binary ? 48 : 45, role == "Scope" ? native.ScopeBytes : native.TreeBytes);
        Assert.Equal(binary ? 24 : 23, exact.Length);
        WriteNative(typeof(TContext).Name, role, native);
    }

    /// <summary>Checks that constructing one invalid complete identity cannot reach any operation command.</summary>
    private async Task RejectAsync<TContext, TIdentity>(
        Func<DbContextOptions<TContext>, TContext> create,
        Func<string, TIdentity> identity,
        string role,
        string invalid
    )
        where TContext : UnicodeIdentityContext<TIdentity>
        where TIdentity : notnull
    {
        // Arrange
        var probe = new UnicodeIdentityCommandProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, create, probe);
        var ordinary = Interlocked
            .Increment(ref s_nextNode)
            .ToString("x8", CultureInfo.InvariantCulture);

        var scope = identity(role == "Scope" ? invalid : ordinary);
        var treeId = identity(role == "TreeId" ? invalid : ordinary);
        var node = new UnicodeIdentityNode<TIdentity> { Id = Interlocked.Increment(ref s_nextNode) };
        var entity = context.Model.FindEntityType(typeof(UnicodeIdentityNode<TIdentity>))!;
        var before = await RegistryCountAsync(context, entity);
        probe.Reset();

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<UnicodeIdentityNode<TIdentity>>()
            .ForScope(scope)
            .InsertRootAsync(node, treeId, CancellationToken.None));

        // Assert
        Assert.Equal(role, Assert.IsType<ArgumentException>(failure).ParamName);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(EntityState.Detached, context.Entry(node).State);
        Assert.Equal(before, await RegistryCountAsync(context, entity));
        Assert.Equal("Initial", node.Name);
        Assert.Equal(default, node.TreeId);
        Assert.Equal(default, node.Scope);
    }

    /// <summary>Counts actual persisted lifecycle rows independently of a failed payload operation.</summary>
    private static Task<int> RegistryCountAsync(
        DbContext context,
        IEntityType entity
    ) => context
        .Set<NestedSetTreeRegistry>(NestedSetTreeRegistryMapping.For(entity).Registry.Name)
        .CountAsync(CancellationToken.None);

    /// <summary>Reads actual stored values with native character and byte functions rather than CLR length.</summary>
    private static Task<UnicodeIdentityLengthRow> ReadNativeAsync(
        DbContext context,
        int nodeId,
        bool scoped
    )
    {
        // WHY: These table identifiers are fixed test model names. CAST decodes the explicit UTF-8 byte converter
        // for the character probe; OCTET_LENGTH still measures the unmodified stored column representation.
        var scope = scoped ? "CAST(`Scope` AS CHAR CHARACTER SET utf8mb4)" : "''";
        var scopeBytes = scoped ? "OCTET_LENGTH(`Scope`)" : "0";
        var treeId = "CAST(`TreeId` AS CHAR CHARACTER SET utf8mb4)";
        var statement = $"SELECT {scope} AS Scope, {treeId} AS TreeId, "
            + $"CHAR_LENGTH({scope}) AS ScopeCharacters, {scopeBytes} AS ScopeBytes, "
            + $"CHAR_LENGTH({treeId}) AS TreeCharacters, OCTET_LENGTH(`TreeId`) AS TreeBytes "
            + $"FROM `{context.GetType().Name}` WHERE `Id` = {{0}}";

        return context
            .Database
            .SqlQueryRaw<UnicodeIdentityLengthRow>(statement, nodeId)
            .SingleAsync(CancellationToken.None);
    }

    /// <summary>Retains independently measured native units in the test result evidence.</summary>
    private void WriteNative(
        string shape,
        string role,
        UnicodeIdentityLengthRow row
    ) => _output.WriteLine(
        $"Engine={Engine}; shape={shape}; role={role}; "
        + $"Scope CHAR_LENGTH={row.ScopeCharacters}, OCTET_LENGTH={row.ScopeBytes}; "
        + $"TreeId CHAR_LENGTH={row.TreeCharacters}, OCTET_LENGTH={row.TreeBytes}.");

    /// <summary>Creates exactly twelve Unicode scalar values occupying twenty-three UTF-16 code units.</summary>
    private static string Supplementary(
        char prefix
    ) => prefix + string.Concat(Enumerable.Repeat("\U0001F600", 11));

    /// <summary>
    /// Separates scalar overflow from lone high and low surrogate errors without source encoding ambiguity.
    /// </summary>
    private static string InvalidText(
        string shape
    ) => shape switch
    {
        "overflow" => Supplementary('o') + "x",
        "high" => "a\uD800",
        "low" => "a\uDC00",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };
}
