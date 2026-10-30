# Unicode identity qualification

The identity guard validates the finalized converter's provider representation
against the mapped maximum length before registry commands. This page records
the 2026-10-20 correctness and native-boundary evidence. It is a length-unit
correction, not Unicode normalization or a universal encoding guarantee.

## Contract

Binary provider values count bytes. Known concrete Doka `char`/`varchar` and
Npgsql character mappings count Unicode scalar values. Both concrete type and
assembly must match; a familiar store-type name does not authorize a custom
mapping. SQL Server, SQLite and unrecognized mappings retain the conservative
UTF-16 guard. Combining marks count separately.

The mapping policy is resolved once per finalized model/role. Requests do not
reflect over mapping types, allocate UTF-8 buffers, replace malformed text or
change identity values. The BMP path uses span scanning; the supplementary path
strictly validates surrogate pairs. Malformed pairs and real overflows retain
role-specific argument errors before structural commands. Native collation,
padding, conversion and physical encoding remain authoritative.

## Executed evidence

The old implementation rejected a valid 254-scalar root identity on MySQL and
MariaDB because it counted 507 UTF-16 units. The initial 125 Unit cases exposed
51 failures with 74 passing controls; the separate PostgreSQL `bpchar(8)` alias
control exposed four failures before its narrow correction.

| Dated corrected-source cohort | Passed | Exclusions |
| --- | ---: | --- |
| Complete Unit project | 2109 | None; includes 205 Unicode cases |
| Selected MySQL/MariaDB identity/registry/collation | 114 | None; includes 52 native cases |
| Ancestor lifecycle/readback/generated model | 156 | 33 SQL Server cases on ARM |

Thirteen new generated-model cases use the provider's actual C# generator,
compile/load the output and execute writes or rejection. Positive/negative
coverage includes independent Scope/TreeId, unscoped models, exact and overflow
boundaries, malformed pairs, ASCII/BMP/supplementary/mixed/combining strings,
converted strings, byte-backed values, custom/clone mappings and misleading
type names. Native cases retain coordinated save, distinct identities, JSON
transport, duplicate rejection and zero-command argument rejection. Four fixed
warm guard counters observe zero allocation over 10,000 operations each; this
does not imply allocation-free complete mutations.

The local provider DLL SHA-256 was
`2af5e7af7fcc5f26c9d62b313137f462853532a86df127f23e7387ca25549aa3`;
its consumer NestedSet DLL was
`7053fbacff2f7b5ccfbf1ac705a79f9cc1b171cbd9837016d3cdcf3445a97efa`.
The independent native-boundary executable used NestedSet DLL
`dfe2a935eeca67f8d42882c5d5022a2e409cdf38309d9ca123214b50433ec139`.
Separate output identities are not represented as one binary.

## Full native boundary

MySQL 8.4.11 and MariaDB 11.8.9 both created the implicit string-key schema
with Id, ParentId, Scope and TreeId at `varchar(254)`. Prefix-free ancestor and
sibling indexes retained their full columns within 3,065 and 3,056 declared
bytes. Public root/child insertion roundtripped all identities, ParentId and
generated branch flags.

Each identity had one ASCII prefix and 253 supplementary scalars:

| Observed measure | Both engines |
| --- | ---: |
| Native character length | 254 |
| .NET UTF-16 units | 507 |
| Native octet length | 1013 |
| Persisted root/child rows | 2 |

Independent 255-scalar Scope and TreeId requests rejected with exact role and
mapped maximum, retaining both persisted rows. The converted binary boundary
accepted 48 bytes and rejected 49. The full implicit-index case requires the
provider's separate index-budget correction. The guard itself also passed all
205 Unit, 52 native and thirteen generated-model cases against published Doka
10.5.0, independently of that provider feature. Neither this run nor model
translation qualifies native SQL Server, hosted release or NativeAOT behavior.

## Current regression boundary

The later 2026-10-24/25 final-product Unit cohort passes 2,611 cases, including
the retained identity/mapping guards, against the new local owner graph. The
separate broader MySQL/MariaDB cohort and native query-work controls remain
identified in the [provider record](provider-migrations.md). These later source
checks do not relabel the dedicated historical Unicode/native-boundary cohort
as a new execution or establish native SQL Server results.

## Reproduce the repository checks

```sh
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~Unicode'

dotnet test tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~Unicode'
```

Use the selected provider graph's declared string facets and encoding, and
verify nonzero case selection. The [provider record](provider-migrations.md)
distinguishes the historical private graph from published baselines. These
commands are procedures, not evidence of a new run.

Primary contracts: [MySQL character types](https://dev.mysql.com/doc/refman/8.4/en/char.html),
[MariaDB VARCHAR](https://mariadb.com/docs/server/reference/data-types/string-data-types/varchar),
[PostgreSQL character types](https://www.postgresql.org/docs/17/datatype-character.html),
[SQL Server Unicode capacities](https://learn.microsoft.com/en-us/sql/t-sql/data-types/nchar-and-nvarchar-transact-sql?view=sql-server-ver17),
and [SQLite type affinity](https://sqlite.org/datatype3.html).
