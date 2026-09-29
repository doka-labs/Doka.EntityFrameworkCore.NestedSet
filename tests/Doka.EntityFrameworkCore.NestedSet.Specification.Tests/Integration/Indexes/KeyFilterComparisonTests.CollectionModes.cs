namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class KeyFilterComparisonTests
{
    /// <summary>Gets native string and binary membership cases under both application collection overrides.</summary>
    public static TheoryData<ParameterTranslationMode, string, bool> CollectionModeCases
    {
        get
        {
            var cases = new TheoryData<ParameterTranslationMode, string, bool>();

            foreach (var mode in new[] { ParameterTranslationMode.Constant, ParameterTranslationMode.Parameter })
            {
                foreach (var key in new[] { "String", "Binary" })
                {
                    cases.Add(mode, key, true);
                    cases.Add(mode, key, false);
                }
            }

            return cases;
        }
    }

    /// <summary>
    /// Structural membership stays parameterized and exact even when application queries use another mode.
    /// </summary>
    /// <param name="mode">The application's collection-translation preference.</param>
    /// <param name="key">The native mapped key representation.</param>
    /// <param name="includeExisting">Whether one requested key identifies a persisted row.</param>
    [Theory]
    [MemberData(nameof(CollectionModeCases))]
    public Task CollectionModesPreserveParameterizedNativeMembership(
        ParameterTranslationMode mode,
        string key,
        bool includeExisting
    ) => key switch
    {
        "String" => CompareCollectionModeAsync<TextNode, string>(
            Engine,
            mode,
            includeExisting,
            static index => "Collection-" + index.ToString("D2", CultureInfo.InvariantCulture),
            static id => new TextNode
            {
                Id = id,
                Tree = "scope",
                TreeId = Guid.NewGuid(),
                Left = 1,
                Right = 2,
            },
            Engine == "PostgreSql" ? null : static value => value.ToLowerInvariant()),
        "Binary" => CompareCollectionModeAsync<BinaryNode, byte[]>(
            Engine,
            mode,
            includeExisting,
            static index => [0, (byte)index, 0xFF],
            static id => new BinaryNode
            {
                Id = id,
                Tree = 1,
                TreeId = Guid.NewGuid(),
                Left = 1,
                Right = 2,
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>
    /// Executes one native membership query with missing-key controls and observes its actual bindings.
    /// </summary>
    /// <typeparam name="TEntity">The existing native-key fixture entity.</typeparam>
    /// <typeparam name="TKey">The unconverted string or binary identity.</typeparam>
    /// <param name="engine">The fixture-owned relational engine.</param>
    /// <param name="mode">The application's collection-translation override.</param>
    /// <param name="includeExisting">Whether the request includes one persisted identity.</param>
    /// <param name="createKey">Creates distinct native values with independent binary storage.</param>
    /// <param name="createNode">Creates valid independent leaf trees using the actual fixture mapping.</param>
    /// <param name="alias">An optional case alias supported by the existing principal key's native collation.</param>
    private async Task CompareCollectionModeAsync<TEntity, TKey>(
        string engine,
        ParameterTranslationMode mode,
        bool includeExisting,
        Func<int, TKey> createKey,
        Func<TKey, TEntity> createNode,
        Func<TKey, TKey>? alias = null
    )
        where TEntity : class
        where TKey : notnull
    {
        // Arrange
        var database = await _fixture.ResetAsync(engine);
        await using var setup = database.CreateContext();

        foreach (var index in Enumerable.Range(1, 3))
        {
            await setup.AddAsync(createNode(createKey(index)), CancellationToken.None);
        }

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var probe = new EnterpriseProbe();
        var providerOptions = ModelCompatibilityDatabase.Options<TreeContext>(
            engine,
            setup.Database.GetConnectionString()!,
            mode);

        var options = new DbContextOptionsBuilder<TreeContext>(providerOptions).AddInterceptors(probe)
            .Options;

        await using var context = new TreeContext(options);
        TKey[] requested = [createKey(includeExisting ? 1 : 4), createKey(5)];

        if (alias is not null)
        {
            // WHY: SQLite, Doka and the SQL Server fixture compare these string principals case-insensitively.
            // The default PostgreSQL fixture is case-sensitive; its explicit CI ordering model is covered below.
            requested = requested
                .Select(alias)
                .ToArray();
        }

        var keyProperty = context.Model.FindEntityType(typeof(TEntity))!.FindProperty("Id")!;

        // Act
        var found = await context
            .Set<TEntity>()
            .Where(NestedSetKeyFilter<TEntity>.Matches(keyProperty, requested))
            .Select(node => EF.Property<TKey>(node, "Id"))
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(includeExisting ? 1 : 0, found.Length);

        if (includeExisting)
        {
            Assert.Equal(CollectionIdentity(createKey(1)), CollectionIdentity(Assert.Single(found)));
        }

        Assert.Single(probe.Commands);
        var parameters = Assert.Single(probe.ParameterValues);

        // WHY: Correct rows alone cannot expose an application Constant override. Scalar bindings prove that
        // neither literal embedding nor one JSON/array parameter replaced the library's bounded key parameters.
        Assert.All(
            requested,
            expected => Assert.Contains(
                parameters,
                actual => actual is string or byte[] && CollectionIdentity(actual) == CollectionIdentity(expected)));
    }

    /// <summary>Compares native identities by value while excluding serialized collection containers.</summary>
    /// <param name="value">The scalar string or binary value.</param>
    /// <returns>The exact textual or binary contents.</returns>
    private static string CollectionIdentity(
        object value
    ) => value is byte[] bytes ? Convert.ToHexString(bytes) : (string)value;
}
