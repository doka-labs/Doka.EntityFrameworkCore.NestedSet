namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies the immutable engine owned by a concrete provider fixture.</summary>
public interface IProviderFixture
{
    /// <summary>Gets the engine used by every case in this concrete suite.</summary>
    string Engine { get; }
}

/// <summary>Exposes an engine-bound resource without coupling shared suites to a concrete provider.</summary>
/// <typeparam name="TResource">The database or test resource owned by the fixture.</typeparam>
public interface IProviderFixture<out TResource> : IProviderFixture
{
    /// <summary>Gets the fixture-owned resource; individual cases must not dispose it.</summary>
    TResource Value { get; }
}

/// <summary>Defines the engine identity at the concrete fixture's type boundary.</summary>
public interface IProviderEngine
{
    /// <summary>Gets the exact engine name accepted by the database helpers.</summary>
    static abstract string Name { get; }
}

/// <summary>Identifies MySQL cases within the Doka provider executable.</summary>
public readonly struct MySqlEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "MySql";
}

/// <summary>Identifies MariaDB cases within the Doka provider executable.</summary>
public readonly struct MariaDbEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "MariaDb";
}

/// <summary>Identifies PostgreSQL cases.</summary>
public readonly struct PostgreSqlEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "PostgreSql";
}

/// <summary>Identifies SQL Server cases.</summary>
public readonly struct SqlServerEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "SqlServer";
}

/// <summary>Identifies SQLite cases.</summary>
public readonly struct SqliteEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "Sqlite";
}

/// <summary>Binds one resource and its asynchronous lifetime to an immutable provider identity.</summary>
/// <typeparam name="TResource">The existing resource implementation, constructed once by this fixture.</typeparam>
/// <typeparam name="TEngine">The engine selected by the concrete provider suite.</typeparam>
public sealed class ProviderFixture<TResource, TEngine> : IProviderFixture<TResource>, IAsyncLifetime
    where TResource : class, new()
    where TEngine : struct, IProviderEngine
{
    /// <inheritdoc />
    public string Engine => TEngine.Name;

    /// <inheritdoc />
    public TResource Value { get; } = new();

    /// <inheritdoc />
    public ValueTask InitializeAsync() => Value is IAsyncLifetime lifetime
        ? lifetime.InitializeAsync()
        : ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // WHY: Existing resources own their databases/files; xUnit must dispose that exact resource once.
        if (Value is IAsyncDisposable asynchronous)
        {
            await asynchronous.DisposeAsync();
        }
        else if (Value is IDisposable synchronous)
        {
            synchronous.Dispose();
        }
    }
}

/// <summary>Supplies immutable engine ownership to metadata-only suites without database resources.</summary>
public sealed class ProviderResources;

/// <summary>Reads engine identity from the fixture supplied by the concrete provider suite.</summary>
public abstract class ProviderTest
{
    /// <summary>Retains the immutable engine identity for shared test methods.</summary>
    /// <param name="fixture">The resource owner supplied through the concrete xUnit constructor.</param>
    protected ProviderTest(
        IProviderFixture fixture
    )
    {
        Engine = fixture.Engine;
    }

    /// <summary>Gets the engine bound to this concrete suite's fixture.</summary>
    protected string Engine { get; }
}
