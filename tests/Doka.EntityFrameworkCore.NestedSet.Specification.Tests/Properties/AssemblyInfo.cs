// WHY: Provider-specific suites and unit tests reuse internal specification helpers without copying sources.
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.Unit.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.MySql.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests")]
