namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Preserves serialization between this engine's independent and same-tree writer scenarios.</summary>
// WHY: Each scenario opens 64 real connections; overlapping them would test server connection capacity
// instead of tree-lock independence. Other collections and the writers within each scenario remain parallel.
[CollectionDefinition]
public sealed class ConcurrentWriterTestDefinition;
