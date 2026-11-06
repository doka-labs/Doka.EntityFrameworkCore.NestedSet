using Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the Doka family's native Scope identity regressions on MariaDB.</summary>
[Collection("Model compatibility")]
public sealed class NativeCollationTests : NativeCollationTestBase
{
    /// <summary>Uses the exact engine fixture registered for this provider suite.</summary>
    /// <param name="fixture">The isolated provider resource from the suite or its collection.</param>
    public NativeCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
