namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Checks unindexed ancestor envelopes against actual MySQL data and consumer compositions.</summary>
[Collection("Model compatibility")]
public sealed class IndependentAncestorEnvelopeTests : IndependentAncestorEnvelopeTestBase
{
    /// <summary>Uses the existing MySQL collection-owned model database.</summary>
    /// <param name="fixture">The exact provider fixture.</param>
    public IndependentAncestorEnvelopeTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
