// WHY: Structural planning and model-cache contracts are verified directly without exposing implementation APIs.

[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.Unit.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.MySql.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.Specification.Tests")]

// WHY: Native adapters implement the common kernel's internal translation and registry capability contracts.
// Their shipping dependencies are pinned to the matching kernel version because these roles share a private ABI.
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.MySql")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.Postgres")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.Sqlite")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.NestedSet.SqlServer")]
