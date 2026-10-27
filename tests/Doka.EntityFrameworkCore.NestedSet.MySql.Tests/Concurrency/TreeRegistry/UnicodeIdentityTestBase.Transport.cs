using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class UnicodeIdentityTestBase
{
    /// <summary>Scalar and JSON rowsets preserve every supplementary Scope and TreeId during native locking.</summary>
    /// <param name="count">Two scalar rows or the exact JSON rowset threshold.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(256)]
    public async Task SupplementaryRowsetsReserveEveryDistinctIdentity(int count)
    {
        // Arrange
        var probe = new UnicodeIdentityCommandProbe();
        await using var context = await _fixture.CreateContextAsync<UnicodeTextIdentityContext>(
            Engine,
            static options => new UnicodeTextIdentityContext(options),
            probe);

        var entity = context.Model.FindEntityType(typeof(UnicodeIdentityNode<string>))!;
        // WHY: A case-owned Scope prevents the two transport variants from ever sharing lifecycle rows, even
        // when this fixture retains their tables for the full provider suite.
        var scope = Interlocked
                .Increment(ref s_nextNode)
                .ToString("x8", CultureInfo.InvariantCulture)
            + string.Concat(Enumerable.Repeat("\U0001F600", 4));

        const string prefix = "tree";
        var requests = Enumerable
            .Range(0, count)
            .Select(index => new NestedSetTreeLockRequest<string, string>(
                entity,
                scope,
                prefix
                + index.ToString("x2", CultureInfo.InvariantCulture)
                + string.Concat(Enumerable.Repeat("\U0001F600", 6)),
                NestedSetTreeLockMode.New))
            .Cast<INestedSetTreeLockRequest>()
            .ToArray();

        var registry = NestedSetTreeRegistryMapping.For(entity)
            .Registry;
        probe.Reset();

        // Act
        await NestedSetTransaction.ExecuteAsync(
            context,
            async token =>
            {
                await NestedSetTreeLocks.RequireDistinctAsync(context, requests, token);
                await NestedSetTreeLocks.AcquireAsync(context, requests, token);
            },
            CancellationToken.None);

        // Assert
        var commandCount = probe.CommandCount;
        var jsonCommands = probe.JsonRowsetCommands;
        var expected = requests
            .Select(request => (string)request.TreeIdValue)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var actual = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.Scope) == scope)
            .Select(row => EF.Property<string>(row, NestedSetTreeRegistryMetadata.TreeId))
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(expected, actual.Order(StringComparer.Ordinal));
        Assert.All(expected, value => Assert.Equal(12, value.EnumerateRunes().Count()));
        Assert.Equal(count == 256 ? 2 : 0, jsonCommands);
        _output.WriteLine($"Engine={Engine}; rowset={count}; exactReservedIdentities={expected.Length}; "
            + $"scopeCharacters=12; scopeUtf16Units={scope.Length}; scopeOctets=24; commands={commandCount}; "
            + $"jsonRowsetCommands={jsonCommands}.");
    }

    /// <summary>
    /// Both transport shapes still reject truly equal supplementary identities without registry writes.
    /// </summary>
    /// <param name="count">Two scalar rows or the exact JSON rowset threshold.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(256)]
    public async Task SupplementaryRowsetsRejectEqualIdentities(int count)
    {
        // Arrange
        var probe = new UnicodeIdentityCommandProbe();
        await using var context = await _fixture.CreateContextAsync<UnicodeTextIdentityContext>(
            Engine,
            static options => new UnicodeTextIdentityContext(options),
            probe);

        var entity = context.Model.FindEntityType(typeof(UnicodeIdentityNode<string>))!;
        var scope = Interlocked
            .Increment(ref s_nextNode)
            .ToString("x8", CultureInfo.InvariantCulture);

        var treeId = Supplementary('d');
        var requests = Enumerable
            .Range(0, count)
            .Select(index => new NestedSetTreeLockRequest<string, string>(
                entity,
                scope,
                index < 2
                    ? treeId
                    : index.ToString("x3", CultureInfo.InvariantCulture)
                    + string.Concat(Enumerable.Repeat("\U0001F600", 9)),
                NestedSetTreeLockMode.New))
            .Cast<INestedSetTreeLockRequest>()
            .ToArray();

        var before = await RegistryCountAsync(context, entity);
        probe.Reset();

        // Act
        var failure = await Record.ExceptionAsync(() => NestedSetTreeLocks.RequireDistinctAsync(
            context,
            requests,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(count == 256 ? 1 : 0, probe.JsonRowsetCommands);
        Assert.Equal(before, await RegistryCountAsync(context, entity));
    }
}
