namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Checks unindexed ancestor envelopes against actual MariaDB data and consumer compositions.</summary>
[Collection("Model compatibility")]
public sealed class IndependentAncestorEnvelopeTests : MySql.IndependentAncestorEnvelopeTestBase
{
    /// <summary>Uses the existing MariaDB collection-owned model database.</summary>
    /// <param name="fixture">The exact provider fixture.</param>
    public IndependentAncestorEnvelopeTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
