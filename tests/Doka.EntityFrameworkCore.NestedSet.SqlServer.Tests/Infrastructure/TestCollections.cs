namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Shares isolated model-compatibility resources within this provider collection.</summary>
[CollectionDefinition("Model compatibility")]
public sealed class ModelCompatibilityTestsDefinition :
    ICollectionFixture<ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine>>;
