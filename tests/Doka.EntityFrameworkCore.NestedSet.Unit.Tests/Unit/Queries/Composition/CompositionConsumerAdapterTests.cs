using Microsoft.EntityFrameworkCore.Storage;
using GroupContext = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups.UserGroupContext;
using SignalDataset = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.SignalReportDataset;
using SignalLabContext = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.SignalLabContext;
using SignalNode = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.SignalNode;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks identity preservation and collision rejection in capacity-only consumer adapters.</summary>
public sealed class CompositionConsumerAdapterTests
{
    /// <summary>A namespaced sample key with ordinal zero cannot alias the unbound empty catalog sentinel.</summary>
    [Fact]
    public void NamespacedZeroKeyDoesNotAliasTheEmptyCatalogSentinel()
    {
        // Arrange
        var sampleZero = SignalDataset.Id(0);

        // Act
        var error = Record.Exception(() => CompositionConsumerModel.Integer(sampleZero));

        // Assert
        Assert.NotEqual(Guid.Empty, sampleZero);
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>Integer boundaries round-trip; GUIDs with a foreign suffix are rejected.</summary>
    /// <param name="value">The fixture integer, including both signed boundaries.</param>
    /// <param name="foreignSuffix">Whether to change the GUID suffix while retaining its integer prefix.</param>
    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MinValue, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    public void IntegerIdentityCodecDoesNotCollapseForeignGuids(
        int value,
        bool foreignSuffix
    )
    {
        // Arrange
        var identity = CompositionConsumerModel.Identity(value);
        var bytes = identity.ToByteArray();

        if (foreignSuffix)
        {
            bytes[^1] ^= 0x7f;
        }

        var expected = value == 0 ? Guid.Empty : new Guid(value, 0x51ab, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1);
        var candidate = new Guid(bytes);
        int? decoded = null;

        // Act
        var error = Record.Exception(() => decoded = CompositionConsumerModel.Integer(candidate));

        // Assert
        Assert.Equal(expected, identity);

        if (foreignSuffix)
        {
            Assert.IsType<ArgumentException>(error);
            Assert.Null(decoded);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal(value, decoded);
        }
    }

    /// <summary>Every application GUID property uses a consistent identity codec or a bijective tree swap.</summary>
    /// <param name="signal">Whether to inspect the actual signal adapter rather than the group adapter.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationConvertersPreserveTreeAndForeignKeyIdentity(
        bool signal
    )
    {
        // Arrange
        using DbContext context = signal
            ? new CompositionCapacitySignalLabContext(
                ModelCompatibilityDatabase.Options<CompositionCapacitySignalLabContext>("Sqlite"))
            : new CompositionCapacityGroupContext(
                ModelCompatibilityDatabase.Options<CompositionCapacityGroupContext>("Sqlite"));

        var mapping = NestedSetModelMapping.For(context.Model);
        var entities = context.Model.GetEntityTypes().Where(entity => entity.ClrType.Assembly
            == (signal ? typeof(SignalLabContext).Assembly : typeof(GroupContext).Assembly)).ToArray();

        var trees = entities.Where(entity => mapping.TryDescriptor(entity, out _))
            .Select(entity => mapping.Descriptor(entity).TreeId.Resolve(entity)).ToHashSet();

        var properties = entities.SelectMany(entity => entity.GetDeclaredProperties())
            .Where(property => property.ClrType == typeof(Guid) || property.ClrType == typeof(Guid?)).ToArray();

        var otherTree = Guid.Parse("55555555-1111-2222-3333-444444444444");
        int[] integers = [int.MinValue, -1, 0, 1, int.MaxValue];

        // Act
        var observations = properties.Select(property =>
        {
            var converter = property.GetValueConverter()
                ?? throw new InvalidOperationException(
                    $"Missing converter for {property.DeclaringType.Name}.{property.Name}.");

            var tree = trees.Contains(property);
            var values = tree
                ? [Guid.Empty, CompositionConsumerModel.Tree, otherTree]
                : integers.Select(CompositionConsumerModel.Identity).ToArray();

            var encoded = values.Select(value => converter.ConvertToProvider(value)).ToArray();
            var decoded = encoded.Select(converter.ConvertFromProvider).ToArray();

            return (tree, converter.ProviderClrType, values, encoded, decoded);
        }).ToArray();

        // Assert
        Assert.NotEmpty(trees);
        Assert.NotEmpty(observations);
        Assert.All(observations, observed =>
        {
            Assert.Equal(observed.values.Cast<object>(), observed.decoded);

            if (observed.tree)
            {
                Assert.Equal(typeof(Guid), observed.ProviderClrType);
                Assert.Equal(new object[] { CompositionConsumerModel.Tree, Guid.Empty, otherTree }, observed.encoded);
            }
            else
            {
                Assert.Equal(typeof(int), observed.ProviderClrType);
                Assert.Equal(integers.Cast<object>(), observed.encoded);
            }
        });
    }

    /// <summary>Unbound UNION literals use the same provider identity as their actual property branches.</summary>
    /// <param name="engine">The provider model, finalized without starting a database.</param>
    /// <param name="signal">Whether unbound catalog keys are integers rather than swapped tree identities.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    public void UnboundScalarIdentityMatchesTheMappedUnionBranch(string engine, bool signal)
    {
        // Arrange
        using DbContext context = signal
            ? new CompositionCapacitySignalLabContext(
                ModelCompatibilityDatabase.Options<CompositionCapacitySignalLabContext>(engine))
            : new CompositionCapacityGroupContext(
                ModelCompatibilityDatabase.Options<CompositionCapacityGroupContext>(engine));

        var entity = context.Model.FindEntityType(signal ? typeof(SignalNode) : typeof(UserGroup))!;

        var descriptor = NestedSetModelMapping.For(context.Model).Descriptor(entity);
        var property = (signal ? descriptor.NodeKey : descriptor.TreeId).Resolve(entity).GetTypeMapping();
        var values = signal
            ? new[]
            {
                Guid.Empty,
                CompositionConsumerModel.Identity(1),
                CompositionConsumerModel.Identity(int.MaxValue),
            }
            : new[]
            {
                Guid.Empty,
                CompositionConsumerModel.Tree,
                Guid.Parse("55555555-1111-2222-3333-444444444444"),
            };

        // Act
        // WHY: A property-only mapping check misses unbound literals in catalog/status UNION branches.
        // Resolve through the actual model's public pre-convention-aware scalar mapping service.
        var scalar = context.GetService<ITypeMappingSource>().FindMapping(typeof(Guid), context.Model)
            ?? throw new InvalidOperationException("The adapter has no unbound Guid scalar mapping.");

        var encoded = values.Select(value => scalar.Converter is { } converter
            ? converter.ConvertToProvider(value) : value).ToArray();

        var expected = values.Select(value => property.Converter is { } converter
            ? converter.ConvertToProvider(value) : value).ToArray();

        var decoded = encoded.Select(value => scalar.Converter is { } converter
            ? converter.ConvertFromProvider(value) : value).ToArray();

        // Assert
        Assert.Equal(expected, encoded);
        Assert.Equal(values.Cast<object>(), decoded);
    }
}
