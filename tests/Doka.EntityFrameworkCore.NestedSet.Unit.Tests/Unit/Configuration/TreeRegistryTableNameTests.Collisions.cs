namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TreeRegistryTableNameTests
{
    /// <summary>Rejects two registries that would share one table, including a case-only difference.</summary>
    /// <param name="otherName">The second hierarchy's explicit name.</param>
    [Theory]
    [InlineData("Shared")]
    [InlineData("shared")]
    public void FinalizeModel_RegistryCollision_IsRejectedWithBothNames(
        string otherName
    )
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Shared");
            Map<SecondNode>(modelBuilder, "Seconds", otherName);
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("collides", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a name already used by an application table.</summary>
    [Fact]
    public void FinalizeModel_ApplicationTableCollision_IsRejected()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Orders");
            modelBuilder
                .Entity<Order>()
                .ToTable("Orders");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Orders", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a name equal to the hierarchy's own table.</summary>
    [Fact]
    public void FinalizeModel_OwnHierarchyTableCollision_IsRejected()
    {
        // Arrange
        using var context = Create(modelBuilder => Map<FirstNode>(modelBuilder, "Firsts", "Firsts"));

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Allows the same name in another schema where the provider creates schemas.</summary>
    /// <param name="provider">A provider whose DDL keeps the schema.</param>
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("Postgres")]
    [InlineData("MySql")]
    public void For_SameNameInAnotherSchema_IsAllowedWhereSchemasExist(
        string provider
    )
    {
        // Arrange
        using var context = CreateFor(
            provider,
            modelBuilder =>
            {
                Map<FirstNode>(modelBuilder, "Firsts", "Trees", "first");
                Map<SecondNode>(modelBuilder, "Seconds", "Trees", "second");
            });

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var first = NestedSetTreeRegistryMapping.For(model.FindEntityType(typeof(FirstNode))!)
            .Registry;

        var second = NestedSetTreeRegistryMapping.For(model.FindEntityType(typeof(SecondNode))!)
            .Registry;

        // Assert
        Assert.Equal(first.GetTableName(), second.GetTableName());
        Assert.NotEqual(first.GetSchema(), second.GetSchema());
    }

    /// <summary>Rejects one name in two schemas on SQLite, whose DDL drops schemas and creates one table.</summary>
    [Fact]
    public void FinalizeModel_SameNameInAnotherSchema_CollidesOnSqlite()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Trees", "first");
            Map<SecondNode>(modelBuilder, "Seconds", "Trees", "second");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("collides", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Compares schema names like table names, so case-only schema differences follow the provider.</summary>
    /// <param name="provider">The provider under test.</param>
    /// <param name="collides">Whether that provider can treat both schemas as one.</param>
    [Theory]
    [InlineData("SqlServer", true)]
    [InlineData("MySql", true)]
    [InlineData("Postgres", false)]
    public void FinalizeModel_SchemaCaseVariant_FollowsTheProvider(
        string provider,
        bool collides
    )
    {
        // Arrange
        using var context = CreateFor(
            provider,
            modelBuilder =>
            {
                Map<FirstNode>(modelBuilder, "Firsts", "Trees", "Tenant");
                Map<SecondNode>(modelBuilder, "Seconds", "Trees", "tenant");
            });

        // Act
        var error = Record.Exception(() => _ = context.GetService<IDesignTimeModel>()
            .Model);

        // Assert
        if (collides)
        {
            Assert.Contains(
                "collides",
                Assert.IsType<InvalidOperationException>(error)
                    .Message);
        }
        else
        {
            Assert.Null(error);
        }
    }

    /// <summary>Keeps names apart that SQLite itself keeps apart, because it folds only ASCII letters.</summary>
    /// <returns>A task that completes after the native schema names have been inspected.</returns>
    [Fact]
    public async Task EnsureCreatedAsync_Sqlite_FoldsOnlyAsciiLetters()
    {
        // Arrange
        await using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "\u00c4Trees");
            Map<SecondNode>(modelBuilder, "Seconds", "\u00e4Trees");
            Map<ThirdNode>(modelBuilder, "Thirds", "\u017fqlite_Trees");
        });

        await context.Database.OpenConnectionAsync(CancellationToken.None);

        // Act
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' ORDER BY name";
        List<string> tables = [];

        await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
        {
            // WHY: The forward-only schema cursor is consumed once while retaining only names for collision checks.
            while (await reader.ReadAsync(CancellationToken.None))
            {
                tables.Add(reader.GetString(0));
            }
        }

        // Assert
        Assert.Contains("\u00c4Trees", tables);
        Assert.Contains("\u00e4Trees", tables);
        Assert.Contains("\u017fqlite_Trees", tables);
    }

    /// <summary>Still rejects an ASCII case variant on SQLite, which SQLite treats as the same table.</summary>
    [Fact]
    public void FinalizeModel_SqliteAsciiCaseVariant_Collides()
    {
        // Arrange
        using var context = Create(modelBuilder =>
        {
            Map<FirstNode>(modelBuilder, "Firsts", "Trees");
            Map<SecondNode>(modelBuilder, "Seconds", "TREES");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.Contains(
            "collides",
            Assert.IsType<InvalidOperationException>(error)
                .Message);
    }
}
