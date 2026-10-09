namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies provider metadata suites or methods that never create or connect to a database.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class DatabaseIndependentAttribute : Attribute;
