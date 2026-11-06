namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies provider metadata suites or methods that never create or connect to a database.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DatabaseIndependentAttribute : Attribute;
