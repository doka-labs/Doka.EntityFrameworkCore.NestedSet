using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Represents an application identity whose stored string includes a converter-owned prefix.</summary>
    /// <param name="Value">The exact application identity contents.</param>
    private readonly record struct ConvertedIdentity(string Value);

    /// <summary>Finalizes real provider mappings while keeping every connection unopened.</summary>
    private sealed class UnicodeContext : DbContext
    {
        private readonly OwnedModelTestServices<UnicodeContext> _services;

        internal const string Raw = "UnicodeRaw";
        internal const string Converted = "UnicodeConverted";
        internal const string Binary = "UnicodeBinary";
        internal const string Scopeless = "UnicodeScopeless";
        internal const string Custom = "UnicodeCustom";
        internal const string Fixed = "UnicodeFixed";

        /// <summary>Uses a configured server version so metadata construction never requires autodetection.</summary>
        internal UnicodeContext(
            string engine
        ) : this(OwnedModelTestServices<UnicodeContext>.Create(Options(engine))) { }

        /// <summary>Binds real provider services with lifetime independent of the global EF cache.</summary>
        private UnicodeContext(
            OwnedModelTestServices<UnicodeContext> services
        ) : base(services.Options)
        {
            _services = services;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            try
            {
                base.Dispose();
            }
            finally
            {
                _services.Dispose();
            }
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                await _services.DisposeAsync();
            }
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var raw = Configure(modelBuilder, Raw);
            raw
                .IndexerProperty<string>("TreeId")
                .HasMaxLength(8)
                .IsRequired();
            raw
                .IndexerProperty<string>("Scope")
                .HasMaxLength(8)
                .IsRequired();
            ConfigureHierarchy(raw, scoped: true);

            var converted = Configure(modelBuilder, Converted);
            converted
                .IndexerProperty<ConvertedIdentity>("TreeId")
                .HasConversion(value => "!" + value.Value, value => new ConvertedIdentity(value.Substring(1)))
                .HasMaxLength(8);
            converted
                .IndexerProperty<ConvertedIdentity>("Scope")
                .HasConversion(value => "!" + value.Value, value => new ConvertedIdentity(value.Substring(1)))
                .HasMaxLength(8);
            ConfigureHierarchy(converted, scoped: true);

            var binary = Configure(modelBuilder, Binary);
            binary
                .IndexerProperty<string>("TreeId")
                .HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value))
                .HasMaxLength(8)
                .IsRequired();
            binary
                .IndexerProperty<string>("Scope")
                .HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value))
                .HasMaxLength(8)
                .IsRequired();
            ConfigureHierarchy(binary, scoped: true);

            var scopeless = Configure(modelBuilder, Scopeless);
            scopeless
                .IndexerProperty<string>("TreeId")
                .HasMaxLength(8)
                .IsRequired();
            ConfigureHierarchy(scopeless, scoped: false);

            var custom = Configure(modelBuilder, Custom);
            custom
                .IndexerProperty<string>("TreeId")
                .HasMaxLength(8)
                .IsRequired()
                .Metadata
                .SetTypeMapping(new StringTypeMapping("varchar(8)", DbType.String, unicode: true, size: 8));
            custom
                .IndexerProperty<string>("Scope")
                .HasMaxLength(8)
                .IsRequired()
                .Metadata
                .SetTypeMapping(new StringTypeMapping("varchar(8)", DbType.String, unicode: true, size: 8));
            ConfigureHierarchy(custom, scoped: true);

            var fixedWidth = Configure(modelBuilder, Fixed);
            fixedWidth
                .IndexerProperty<string>("TreeId")
                .HasColumnType("char(8)")
                .HasMaxLength(8)
                .IsRequired();
            fixedWidth
                .IndexerProperty<string>("Scope")
                .HasColumnType("char(8)")
                .HasMaxLength(8)
                .IsRequired();
            ConfigureHierarchy(fixedWidth, scoped: true);
        }

        /// <summary>Defines the structural properties shared by the model-only hierarchy variants.</summary>
        private static EntityTypeBuilder<Dictionary<string, object>> Configure(
            ModelBuilder modelBuilder,
            string name
        )
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(name);
            entity.ToTable(name);
            entity
                .IndexerProperty<int>("Id")
                .ValueGeneratedNever();
            entity.IndexerProperty<int?>("ParentId");
            entity.IndexerProperty<long>("Left");
            entity.IndexerProperty<long>("Right");
            entity.IndexerProperty<int>("Depth");
            entity.IndexerProperty<long>("Position");
            entity.HasKey("Id");

            return entity;
        }

        /// <summary>Maps either exact scoped identities or a genuine hierarchy without a Scope property.</summary>
        private static void ConfigureHierarchy(
            EntityTypeBuilder<Dictionary<string, object>> entity,
            bool scoped
        ) => entity.HasNestedSet(node =>
        {
            node
                .HasNodeKey("Id")
                .HasTreeId("TreeId")
                .HasParent("ParentId")
                .HasBounds("Left", "Right")
                .HasDepth("Depth")
                .HasPosition("Position");

            if (scoped)
            {
                node.HasScope("Scope");
            }
        });

        /// <summary>Selects actual provider metadata without opening a server connection.</summary>
        private static DbContextOptions<UnicodeContext> Options(
            string engine
        )
        {
            var builder = new DbContextOptionsBuilder<UnicodeContext>();

            switch (engine)
            {
                case "MySql":
                    builder.UseMySql(
                        "Server=127.0.0.1;Port=1;Database=unicode;User ID=unused;Password=unused;",
                        MySqlServerVersion.MySql(new Version(8, 4, 0)), provider => provider.UseNestedSets());
                    break;
                case "MariaDb":
                    builder.UseMySql(
                        "Server=127.0.0.1;Port=1;Database=unicode;User ID=unused;Password=unused;",
                        MySqlServerVersion.MariaDb(new Version(11, 8, 0)), provider => provider.UseNestedSets());
                    break;
                case "PostgreSql":
                    builder.UseNpgsql(
                        "Host=127.0.0.1;Port=1;Database=unicode;Username=unused;Password=unused",
                        provider => provider.UseNestedSets());
                    break;
                case "SqlServer":
                    builder.UseSqlServer(
                        "Server=127.0.0.1,1;Database=unicode;User ID=unused;Password=unused;",
                        provider => provider.UseNestedSets());
                    break;
                case "Sqlite":
                    builder.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(engine));
            }

            builder.ConfigureTestWarnings();

            return builder.Options;
        }
    }
}
