namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Shares one isolated model-compatibility database within this provider assembly.</summary>
[CollectionDefinition("Model compatibility")]
public sealed class
    ModelCompatibilityTestsDefinition : ICollectionFixture<
    ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine>>;
