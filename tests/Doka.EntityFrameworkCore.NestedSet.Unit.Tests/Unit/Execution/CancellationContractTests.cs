using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.NestedSet.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Requires explicit cancellation choices without changing framework contracts or token semantics.</summary>
public sealed class CancellationContractTests
{
    private const BindingFlags DeclaredMembers = BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>Owned methods in every package require an explicit token, including internal execution paths.</summary>
    /// <param name="assemblyName">The package assembly whose declared contracts are inspected.</param>
    [Theory]
    [InlineData("Doka.NestedSet")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.MySql")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.Postgres")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.Sqlite")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.SqlServer")]
    public void OwnedPackageTokensAreRequired(
        string assemblyName
    )
    {
        // Arrange
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        // Act
        var optional = assembly
            .GetTypes()
            .SelectMany(type => type
                .GetMethods(DeclaredMembers)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(DeclaredMembers)))

            // WHY: C# async base-call bridges copy EF's optional parameter metadata; they are compiler output.
            .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(member => !IsFrameworkContract(member))
            .SelectMany(member => member.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(CancellationToken)
                && (parameter.IsOptional || parameter.HasDefaultValue))
            .Select(parameter => $"{parameter.Member.DeclaringType}.{parameter.Member.Name}: {parameter.Name}")
            .ToArray();

        // Assert
        Assert.Empty(optional);
    }

    /// <summary>Async operations need tokens; only exact mandatory completion paths remain tokenless.</summary>
    /// <param name="assemblyName">The package assembly whose async contracts are inspected.</param>
    [Theory]
    [InlineData("Doka.NestedSet")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.MySql")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.Postgres")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.Sqlite")]
    [InlineData("Doka.EntityFrameworkCore.NestedSet.SqlServer")]
    public void OwnedAsyncOperationsRequireTokensUnlessFinalizing(
        string assemblyName
    )
    {
        // Arrange
        var assembly = Assembly.Load(new AssemblyName(assemblyName));
        string[] expected = assemblyName == "Doka.EntityFrameworkCore.NestedSet"
            ?
            [
                "Doka.EntityFrameworkCore.NestedSet.Diagnostics.NestedSetTelemetry.TrackRows",
                "Doka.EntityFrameworkCore.NestedSet.Diagnostics.NestedSetTelemetry.TrackRowsAsync",
                "Doka.EntityFrameworkCore.NestedSet.Execution.NestedSetTransaction.RollbackSavepointAsync",
            ]
            : [];

        // Act
        var tokenless = assembly
            .GetTypes()

            // WHY: Compiler-generated closures carry captured tokens; their signatures are not authored APIs.
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .SelectMany(type => type.GetMethods(DeclaredMembers))
            .Where(method => !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                && !IsFrameworkContract(method)
                && IsAsyncResult(method.ReturnType)
                && !method
                    .GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(CancellationToken)))
            .Select(method => $"{method.DeclaringType}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(expected, tokenless);
    }

    /// <summary>Caller cancellation cannot abandon an already-started write before its final accounting.</summary>
    [Fact]
    public async Task CommandFinalizationWaitsForTerminalResultAfterCancellation()
    {
        // Arrange
        await using var context = new TokenContext();
        using var cancellation = new CancellationTokenSource();
        var command = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var trackedSeparately = false;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NestedSetDiagnostics.ActivitySourceName,
            Sample = (ref _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(listener);

        // Act
        var operation = NestedSetTelemetry.ExecuteAsync(
            context,
            "insert",
            _ =>
            {
                var tracked = NestedSetTelemetry.TrackRows(command.Task);
                trackedSeparately = !ReferenceEquals(tracked, command.Task);

                return tracked;
            },
            cancellation.Token);

        await cancellation.CancelAsync();
        var pendingAfterCancellation = !operation.IsCompleted;
        command.SetResult(7);
        var result = await operation;

        // Assert
        Assert.True(trackedSeparately);
        Assert.True(pendingAfterCancellation);
        Assert.Equal(7, result);
    }

    /// <summary>Terminal provider cancellation is still observed with its exact exception and token.</summary>
    [Fact]
    public async Task CommandFinalizationPreservesProviderCancellation()
    {
        // Arrange
        await using var context = new TokenContext();
        using var cancellation = new CancellationTokenSource();
        var command = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new OperationCanceledException(cancellation.Token);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NestedSetDiagnostics.ActivitySourceName,
            Sample = (ref _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(listener);

        // Act
        var operation = NestedSetTelemetry.ExecuteAsync(
            context,
            "insert",
            _ => NestedSetTelemetry.TrackRows(command.Task),
            cancellation.Token);

        await cancellation.CancelAsync();
        command.SetException(failure);
        var exception = await Record.ExceptionAsync(() => operation);

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(exception);
        Assert.Same(failure, canceled);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    /// <summary>Real EF overrides and interface implementations retain their upstream optional contract.</summary>
    /// <param name="type">The type implementing the actual EF contract.</param>
    /// <param name="methodName">The declared framework method to inspect.</param>
    [Theory]
    [InlineData(typeof(NestedSetDbContext), nameof(DbContext.SaveChangesAsync))]
    [InlineData(typeof(ProviderQueryProbe), "ExecuteAsync")]
    public void FrameworkContractsRetainOptionalTokens(
        Type type,
        string methodName
    )
    {
        // Arrange
        var method = type
            .GetMethods(DeclaredMembers)
            .Single(member => member.Name == methodName
                && member
                    .GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(CancellationToken)));

        // Act
        var token = method
            .GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(CancellationToken));

        // Assert
        Assert.True(IsFrameworkContract(method));
        Assert.True(token.IsOptional);
        Assert.True(token.HasDefaultValue);
    }

    /// <summary>Omission cannot fall back to an implicit token through reflection's optional-argument binder.</summary>
    [Fact]
    public async Task OwnedSaveBoundaryRejectsAnOmittedToken()
    {
        // Arrange
        var method = typeof(NestedSetDbContextExtensions)
            .GetMethod(nameof(NestedSetDbContextExtensions.SaveNestedSetChangesAsync))!;

        // WHY: Binder rejection precedes method execution; no context or save delegate should be consumed.
        object?[] arguments = [null, false, null, Type.Missing];

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await (Task<int>)method.Invoke(null, arguments)!;
        });

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>Required parameters still accept None and forward a live operation token unchanged.</summary>
    /// <param name="cancellable">Whether the caller supplies a live token instead of explicit None.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveBoundaryForwardsExplicitToken(
        bool cancellable
    )
    {
        // Arrange
        await using var context = new TokenContext();
        using var cancellation = new CancellationTokenSource();
        var token = cancellable
            ? cancellation.Token
            : CancellationToken.None;

        var observed = CancellationToken.None;
        var calls = 0;

        // Act
        var saved = await context.SaveNestedSetChangesAsync(
            false,
            received =>
            {
                observed = received;
                calls++;

                return Task.FromResult(7);
            },
            token);

        // Assert
        Assert.Equal(7, saved);
        Assert.Equal(1, calls);
        Assert.Equal(token, observed);
    }

    /// <summary>A canceled explicit token retains its identity and rejects before invoking the save delegate.</summary>
    [Fact]
    public async Task CanceledTokenRejectsBeforeSaveDelegate()
    {
        // Arrange
        await using var context = new TokenContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var calls = 0;

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveNestedSetChangesAsync(
            false,
            _ =>
            {
                calls++;

                return Task.FromResult(7);
            },
            cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, calls);
    }

    /// <summary>Identifies inherited EF/.NET signatures rather than exempting arbitrary owned overrides.</summary>
    private static bool IsFrameworkContract(
        MethodBase member
    )
    {
        if (member is not MethodInfo method)
        {
            return false;
        }

        if (IsFrameworkAssembly(
            method.GetBaseDefinition()
                .DeclaringType!.Assembly))
        {
            return true;
        }

        var type = method.DeclaringType!;

        return type
            .GetInterfaces()
            .Where(contract => IsFrameworkAssembly(contract.Assembly))
            .Any(contract => type
                .GetInterfaceMap(contract)
                .TargetMethods
                .Contains(method));
    }

    /// <summary>Recognizes the external framework owners of cancellation contracts.</summary>
    private static bool IsFrameworkAssembly(
        Assembly assembly
    ) => assembly.GetName()
            .Name is { } name
        && (name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name.StartsWith("System.", StringComparison.Ordinal));

    /// <summary>Recognizes deferred async work without mistaking synchronous query factories for execution.</summary>
    private static bool IsAsyncResult(
        Type type
    ) => typeof(Task).IsAssignableFrom(type)
        || type == typeof(ValueTask)
        || (type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ValueTask<>)
                || type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
                || type.GetGenericTypeDefinition() == typeof(IAsyncEnumerator<>)));

    /// <summary>Provides actual EF services for a save delegate without opening a database connection.</summary>
    private sealed class TokenContext : DbContext
    {
        /// <summary>Registers the SQLite adapter while keeping the model free of persistence work.</summary>
        internal TokenContext() : base(
            new DbContextOptionsBuilder<TokenContext>()
                .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
                .Options) { }
    }
}
