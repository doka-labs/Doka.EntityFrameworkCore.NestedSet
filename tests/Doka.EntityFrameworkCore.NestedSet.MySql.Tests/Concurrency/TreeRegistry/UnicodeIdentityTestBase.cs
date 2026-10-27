namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Verifies Unicode identity bounds and transport using the fixture's actual MySQL-family engine.</summary>
public abstract partial class UnicodeIdentityTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;
    private readonly ITestOutputHelper _output;
    private static int s_nextNode = 7000000;

    /// <summary>Uses the existing collection owner without creating a second provider container.</summary>
    /// <param name="fixture">The immutable engine-bound compatibility database.</param>
    /// <param name="output">The native stored-value and length evidence sink.</param>
    protected UnicodeIdentityTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _output = output;
    }

    /// <summary>
    /// Native character bounds accept supplementary values without conflating neighboring identities.
    /// </summary>
    /// <param name="shape">The ordinary text, converted text, or converted binary storage shape.</param>
    /// <param name="role">The independent identity role receiving the supplementary value.</param>
    [Theory]
    [InlineData("text", "Scope")]
    [InlineData("text", "TreeId")]
    [InlineData("converted", "Scope")]
    [InlineData("converted", "TreeId")]
    [InlineData("binary", "Scope")]
    [InlineData("binary", "TreeId")]
    public Task SupplementaryIdentityRoundTripsWithoutAliasing(
        string shape,
        string role
    ) => shape switch
    {
        "text" => RoundTripAsync<UnicodeTextIdentityContext, string>(
            static options => new UnicodeTextIdentityContext(options),
            static value => value,
            role),
        "converted" => RoundTripAsync<UnicodeConvertedIdentityContext, UnicodeIdentityValue>(
            static options => new UnicodeConvertedIdentityContext(options),
            static value => new UnicodeIdentityValue(value),
            role),
        "binary" => RoundTripAsync<UnicodeBinaryIdentityContext, string>(
            static options => new UnicodeBinaryIdentityContext(options),
            static value => value,
            role),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>
    /// Overflow and malformed provider text fail before any payload or registry command is attempted.
    /// </summary>
    /// <param name="converted">Whether a value object is converted to the checked provider text.</param>
    /// <param name="role">The independently rejected Scope or TreeId role.</param>
    /// <param name="invalid">The overflowing or malformed provider value.</param>
    [Theory]
    [InlineData(false, "Scope", "overflow")]
    [InlineData(false, "TreeId", "overflow")]
    [InlineData(false, "Scope", "high")]
    [InlineData(false, "TreeId", "high")]
    [InlineData(false, "Scope", "low")]
    [InlineData(false, "TreeId", "low")]
    [InlineData(true, "Scope", "overflow")]
    [InlineData(true, "TreeId", "overflow")]
    [InlineData(true, "Scope", "high")]
    [InlineData(true, "TreeId", "high")]
    [InlineData(true, "Scope", "low")]
    [InlineData(true, "TreeId", "low")]
    public Task InvalidProviderTextIsRejectedBeforeNativeCommands(
        bool converted,
        string role,
        string invalid
    ) => converted
        ? RejectAsync<UnicodeConvertedIdentityContext, UnicodeIdentityValue>(
            static options => new UnicodeConvertedIdentityContext(options),
            static value => new UnicodeIdentityValue(value),
            role,
            InvalidText(invalid))
        : RejectAsync<UnicodeTextIdentityContext, string>(
            static options => new UnicodeTextIdentityContext(options),
            static value => value,
            role,
            InvalidText(invalid));

    /// <summary>
    /// A byte-backed converter retains its actual byte bound rather than accepting character overflow.
    /// </summary>
    /// <param name="role">The independently rejected binary identity role.</param>
    [Theory]
    [InlineData("Scope")]
    [InlineData("TreeId")]
    public Task ConvertedBinaryOverflowIsRejectedBeforeNativeCommands(
        string role
    ) => RejectAsync<UnicodeBinaryIdentityContext, string>(
        static options => new UnicodeBinaryIdentityContext(options),
        static value => value,
        role,
        "a" + string.Concat(Enumerable.Repeat("\U0001F600", 12)));

    /// <summary>The optional Scope is unnecessary for accepting the full native TreeId character bound.</summary>
    [Fact]
    public async Task ScopelessTreeIdUsesNativeCharacterLength()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<UnicodeScopelessIdentityContext>(
            Engine,
            static options => new UnicodeScopelessIdentityContext(options));

        var node = new UnicodeIdentityNode<string> { Id = Interlocked.Increment(ref s_nextNode) };
        var treeId = Supplementary('s');

        // Act
        await context
            .NestedSet<UnicodeIdentityNode<string>>()
            .InsertRootAsync(node, treeId, CancellationToken.None);

        // Assert
        var stored = await context
            .NestedSet<UnicodeIdentityNode<string>>()
            .InTree(treeId)
            .Nodes
            .SingleAsync(CancellationToken.None);

        Assert.Equal(node.Id, stored.Id);
        Assert.Equal(treeId, stored.TreeId);
        Assert.Equal((1L, 2L, 0, 0L), (stored.Left, stored.Right, stored.Depth, stored.Position));
        var native = await ReadNativeAsync(context, node.Id, false);
        Assert.Equal(treeId, native.TreeId);
        Assert.Equal((12, 45), (native.TreeCharacters, native.TreeBytes));
        WriteNative("scopeless", "TreeId", native);
    }

    /// <summary>A scopeless overflow is rejected before even reserving the invalid TreeId.</summary>
    [Fact]
    public async Task ScopelessOverflowDoesNotReserveTreeId()
    {
        // Arrange
        var probe = new UnicodeIdentityCommandProbe();
        await using var context = await _fixture.CreateContextAsync<UnicodeScopelessIdentityContext>(
            Engine,
            static options => new UnicodeScopelessIdentityContext(options),
            probe);

        var node = new UnicodeIdentityNode<string> { Id = Interlocked.Increment(ref s_nextNode) };
        var entity = context.Model.FindEntityType(typeof(UnicodeIdentityNode<string>))!;
        var before = await RegistryCountAsync(context, entity);
        probe.Reset();

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<UnicodeIdentityNode<string>>()
            .InsertRootAsync(node, Supplementary('s') + "x", CancellationToken.None));

        // Assert
        Assert.Equal("TreeId", Assert.IsType<ArgumentException>(failure).ParamName);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(EntityState.Detached, context.Entry(node).State);
        Assert.Equal(before, await RegistryCountAsync(context, entity));
    }
}
