using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderTestContractTests
{
    /// <summary>Supplies complete source rows for the raw-ownership and shared-exclusion probes.</summary>
    public static class RawSources
    {
        /// <summary>Supplies no rows to verify that empty provider declarations remain errors.</summary>
        public static IEnumerable<object?[]> EmptyRows => [];

        /// <summary>Supplies a foreign-looking first value that remains ordinary scenario payload.</summary>
        public static IEnumerable<object?[]> PayloadRows => [["PostgreSql"]];

        /// <summary>Supplies a variant restricted to both engines in one Doka executable.</summary>
        public static IEnumerable<ITheoryDataRow> FamilyConditionalRows =>
        [
            new EngineTheoryDataRow(
                [17],
                ["PostgreSql", "SqlServer", "Sqlite"],
                "The probe requires a MySQL-family backend capability."),
        ];

        /// <summary>Supplies one explained, valid conditional shared row.</summary>
        public static IEnumerable<ITheoryDataRow> ValidConditionalRows =>
        [
            new EngineTheoryDataRow([17], ["Sqlite"], "The assertion requires a server execution plan."),
        ];

        /// <summary>Supplies a conditional row with an unknown excluded engine.</summary>
        public static IEnumerable<ITheoryDataRow> UnknownConditionalRows =>
        [
            new EngineTheoryDataRow([17], ["Postgres"], "The assertion requires a server execution plan."),
        ];

        /// <summary>Supplies an unexplained exclusion to exercise the nonempty reason requirement.</summary>
        public static IEnumerable<ITheoryDataRow> UnexplainedConditionalRows =>
        [
            new EngineTheoryDataRow([17], ["Sqlite"], ""),
        ];
    }

    /// <summary>Provides a custom fact marker which the raw fixture-ownership guard must reject.</summary>
    public sealed class UnsupportedFactAttribute : FactAttribute
    {
        /// <summary>Allows reflected metadata to retain the unsupported concrete marker type.</summary>
        /// <param name="sourceFilePath">The compiler-supplied source path.</param>
        /// <param name="sourceLineNumber">The compiler-supplied source line.</param>
        public UnsupportedFactAttribute(
            [CallerFilePath] string? sourceFilePath = null,
            [CallerLineNumber] int sourceLineNumber = -1
        ) : base(sourceFilePath, sourceLineNumber) { }
    }

    /// <summary>Provides custom data which the guard must reject instead of interpreting as engine ownership.</summary>
    public sealed class UnsupportedDataAttribute : DataAttribute
    {
        /// <summary>Allows reflected metadata to retain the unsupported concrete data type.</summary>
        public UnsupportedDataAttribute() { }

        /// <inheritdoc />
        public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(
            MethodInfo testMethod,
            DisposalTracker disposalTracker
        ) => ValueTask.FromResult<IReadOnlyCollection<ITheoryDataRow>>([new TheoryDataRow("PostgreSql")]);

        /// <inheritdoc />
        public override bool SupportsDiscoveryEnumeration() => true;
    }
}
