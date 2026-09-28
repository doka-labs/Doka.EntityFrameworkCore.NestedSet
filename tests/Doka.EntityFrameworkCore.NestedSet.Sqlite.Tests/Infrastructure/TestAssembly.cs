// WHY: xUnit reads assembly fixtures from the executable provider project, not referenced specifications.

[assembly: AssemblyFixture(typeof(Specifications.TestDatabaseServers))]
