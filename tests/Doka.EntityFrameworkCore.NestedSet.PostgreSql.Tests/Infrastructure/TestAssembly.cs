// WHY: xUnit reads assembly fixtures from the executable provider project, not referenced specifications.
[assembly: Xunit.AssemblyFixture(typeof(Specifications.TestDatabaseServers))]
