using Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs overflow-safe Doka subtree contracts on MariaDb.</summary>
[Collection("Model compatibility")]
public sealed class MalformedSubtreeTests : MalformedSubtreeTestBase
{
    /// <summary>Uses the MariaDb-owned model database.</summary>
    /// <param name="fixture">The exact MariaDb provider fixture.</param>
    public MalformedSubtreeTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
