namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises captured collations with generated runtime metadata and a forbidden rebuilding path.</summary>
public class CompiledTreeContext : NestedSetDbContext
{
    /// <summary>Whether this runtime context rejects accidental design-model reconstruction.</summary>
    private readonly bool _rejectModelBuilding;

    /// <summary>Creates the design-time context used to regenerate the checked-in compiled test model.</summary>
    public CompiledTreeContext() : this(
        new DbContextOptionsBuilder<CompiledTreeContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options,
        false) { }

    /// <summary>Creates a context with an explicit generated model and optional reconstruction guard.</summary>
    /// <param name="options">Provider and explicit runtime-model configuration.</param>
    /// <param name="rejectModelBuilding">Whether any model construction is a test failure.</param>
    public CompiledTreeContext(
        DbContextOptions<CompiledTreeContext> options,
        bool rejectModelBuilding
    ) : this((DbContextOptions)options, rejectModelBuilding) { }

    /// <summary>Shares the same domain model with provider-specific generated-model contexts.</summary>
    /// <param name="options">The derived context's real provider configuration.</param>
    /// <param name="rejectModelBuilding">Whether this runtime instance requires a generated model.</param>
    protected CompiledTreeContext(
        DbContextOptions options,
        bool rejectModelBuilding
    ) : base(options)
    {
        _rejectModelBuilding = rejectModelBuilding;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        if (_rejectModelBuilding)
        {
            throw new InvalidOperationException("The compiled runtime model must not be rebuilt.");
        }

        if (Database.IsNpgsql())
        {
            // WHY: PostgreSQL requires an explicit nondeterministic ICU collation for case-insensitive key aliases.
            modelBuilder.HasCollation(
                "nestedset_compiled_ci",
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var keyCollation = Database.IsSqlite()
            ? "NOCASE"
            : Database.IsNpgsql()
                ? "nestedset_compiled_ci"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AS"
                    : "utf8mb4_unicode_ci";

        var parentCollation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_BIN2"
                    : "utf8mb4_bin";

        var anchor = modelBuilder.Entity<CompiledScope>();
        anchor
            .ToTable("CompiledScopes")
            .Property(row => row.Id)
            .HasMaxLength(64)
            .UseCollation(keyCollation)
            .ValueGeneratedNever();

        var node = modelBuilder.Entity<CompiledFolder>();
        node
            .ToTable("CompiledFolders")
            .Property(row => row.Id)
            .HasMaxLength(64)
            .UseCollation(keyCollation)
            .ValueGeneratedNever();

        node
            .Property(row => row.ParentId)
            .HasMaxLength(64)
            .UseCollation(parentCollation);

        node
            .Property(row => row.Scope)
            .HasMaxLength(64)
            .UseCollation(keyCollation);

        if (Database.ProviderName == NestedSetProviderCapabilities.MySqlProviderName)
        {
            // WHY: The real generated Doka model must capture a known physical table default as well as explicit
            // key/parent column collations; runtime-only tests prohibit rebuilding the design metadata.
            node.HasAnnotation(RelationalAnnotationNames.Collation, keyCollation);
            node
                .Property(row => row.Scope)
                .UseCollation(null);
        }

        node
            .Property(row => row.Name)
            .HasMaxLength(128);

        node.HasNestedSet(builder => builder
            .HasTreeId(row => row.TreeId)
            .HasScope(row => row.Scope)
            .HasParent(row => row.ParentId)
            .OrderBy(row => row.Name));

        var numeric = modelBuilder.Entity<CompiledNumber>();
        numeric
            .ToTable("CompiledNumbers")
            .Property(row => row.Id)
            .ValueGeneratedNever();

        numeric.HasNestedSet(builder => builder
            .HasTreeId(row => row.TreeId)
            .HasScope(row => row.Scope)
            .HasParent(row => row.ParentId));
    }
}

/// <summary>A string scope whose aliases follow an explicit case-insensitive database collation.</summary>
public sealed class CompiledScope
{
    /// <summary>Gets or sets the persistent application-owned anchor key.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>A string-key hierarchy with deliberately different parent and primary-key collations.</summary>
public sealed class CompiledFolder : INestedSetNode<string, Guid>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the scope identifying the stable anchor.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets a parent key that may use a case alias.</summary>
    public string? ParentId { get; set; }

    /// <summary>Gets or sets the sibling sort criterion.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}

/// <summary>A numeric-key baseline verifying compiled metadata does not require string-specific paths.</summary>
public sealed class CompiledNumber : INestedSetNode<int, Guid>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the numeric hierarchy scope.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the direct parent key.</summary>
    public int? ParentId { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
