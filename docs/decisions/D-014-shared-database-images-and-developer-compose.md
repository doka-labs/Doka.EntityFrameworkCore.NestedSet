---
id: D-014
status: implemented
date: 2026-09-29
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Shared database image pins, optional developer Compose, and test resource ownership"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-014 -- Share database image pins with optional developer Compose

## Context and Problem Statement

Tests and benchmarks already embed one FROM-only Dockerfile and launch the
selected vendor image through Testcontainers. The image source lived under
tests/Shared and appeared as a resource in consumer project trees. The owner
requested an optional developer database environment following the Doka
provider's test-owned lifecycle, with all Docker files visible under
Config/docker in Rider.

The bounded question is how to enable manual debugging and database inspection
without duplicating image pins or coupling automated test isolation to a
developer-managed server. Benchmark measurement boundaries must also survive.

## Decision Drivers

- Keep automated test and benchmark resources owned by their existing fixtures.
- Maintain exactly one source for tags, digests, and Doka capability versions.
- Preserve existing automated Docker dependency updates.
- Make developer database selection, connections, persistence, and reset clear.
- Avoid a YAML parser, new dependency, custom container launcher, or timing gate.
- Present shared Docker resources once in the solution configuration.

## Considered Options

- Direct Testcontainers plus optional Compose using shared Docker build stages
- Move automated fixtures to Testcontainers Compose
- Keep direct Testcontainers without a developer Compose environment

## Decision Outcome

Chosen option: "Direct Testcontainers plus optional Compose using shared Docker build stages",
because it adds the requested developer access while preserving automated
ownership and sharing the existing dependency-update source.

Keep the four pins in docker/database-images.Dockerfile. Its
independent mysql, mariadb, postgres, and sqlserver stages contain only FROM
instructions. Testcontainers reads these vendor references from embedded data;
Compose uses the file as a real build input with a selected build.target.
BuildKit resolves only the selected stage and its dependencies. Compose's
local service images are derived from the pinned vendor bases; they are not
claimed to have the same image digest as those bases.

Keep each provider assembly's server-per-engine lifetime and fixture database
isolation. The benchmark launcher retains its separately owned container outside
measured child processes. Neither consumer attaches to developer Compose.
SQLite remains in process. Do not enable cross-run Testcontainers resource reuse.

The developer profiles provide SQL readiness, configurable loopback ports,
explicit local credentials, and project-scoped persistent volumes. Starting a
profile uses --build --wait. Stopping preserves developer volumes; explicit
reset removes only the selected Compose project's volumes. Multiple checkouts
need distinct project names and ports. SQL Server keeps Microsoft's Linux x64
support boundary; success under Arm emulation is local smoke evidence only.

The 2026-10-09 amendment selects SQL Server 2022 CU27 on Ubuntu 22.04. The
sqlserver stage uses `mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04`
with digest `sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090`.
Microsoft's build inventory identifies CU27 as 16.0.4295.3. Tests, benchmarks,
and Compose retain this one canonical pin; no per-consumer override is added.
Existing developer volumes are not migrated by the pin change. Use a fresh
SQL Server 2022 volume and preserve needed 2025 data before any explicit reset.

Earlier SQL Server 2025 CU9 Rider runs lost containers during startup with exit
code 1, Reason 0x00000002, parameter 0x10010, and an LSASS stack on both Ubuntu
bases. The open vendor-repository report also reproduces crashes on native
GitHub-hosted Linux x64. ARM is therefore not an established sole cause. The
2026-10-08 comparison passed 36 starts per base, but a later Rider failure on
Ubuntu 22.04 disproved that proposed correction. The mechanism remains
unresolved, and the 2022 selection is not a confirmed vendor fix for CU9.

The private candidate assessment preceding the approved change passed 1,072 SQL
Server provider cases, 10 ordinary SQL Server migration cases, 51 SQL Server
SafeMigrations cases, and 30 startup attempts under macOS ARM x86-64 emulation.
This is limited candidate evidence on an unsupported environment, not native
x64 CI qualification or proof of startup reliability. Native Linux x64 CI must
qualify the new baseline with every applicable SQL Server case and zero skips.
An unsupported CI platform fails. Local ARM database cases remain visible and
skip at runtime before container startup; metadata and unit cases remain runnable
under [D-013](D-013-provider-owned-integration-test-projects.md).
The optional Compose profile remains a manual `linux/amd64` operation. Do not
mask startup failures with retries, dump-capture permission changes, or longer
readiness timeouts. Re-evaluate future baselines against vendor evidence and
native Linux x64 qualification.

PostgreSQL readiness connects through its Compose service address because the
vendor image trusts loopback by default. The probe must validate the configured
password against the account stored in the volume, including after a container
recreation. MySQL and MariaDB probe their scoped application user, not root.

Dependabot monitors /docker. A shared external .env image list was not selected:
the official Docker parsers do not currently resolve those image variables,
which would lose the existing automated updates. Credentials and port overrides
do not become a second image source. No YAML package or general Dockerfile
parser is introduced. The embedded reader validates exactly the four tagged,
SHA-256-pinned FROM stages before resource acquisition.

Show the Dockerfile, Compose file, build-context exclusions, and operating guide
under Config/docker. Mark the three embedded resources Visible=false to retain
embedding without project-tree duplicates. Microsoft documents this metadata;
solution structure and evaluated MSBuild metadata establish the intended layout,
while a Rider UI readback remains a separate presentation check.

### Consequences

- Good, because manual debugging and automated runs use one maintained image source.
- Good, because developer persistence cannot change test isolation or benchmark ownership.
- Bad, because Compose adds a local build/export step after pin updates.
- Bad, because developer ports and persistent credentials require deliberate
  coordination across checkouts and explicit volume reset when initialization changes.

### Confirmation

- Run `docker compose -f docker/compose.yml config --quiet`; expect a valid native
  configuration with four independently selected stages and loopback bindings.
- Start each selected profile with `--build --wait` in a unique temporary project;
  expect SQL readiness and a successful `SELECT 1`. Stop and remove only its owned
  volumes, then verify no containers or volumes for that project remain.
- Build the specification, ordinary migration, and benchmark consumers freshly;
  expect NestedSet.TestImages in each assembly and Visible=false in evaluated
  resource metadata. The solution must list all Docker resources once under Config/docker.
- Run `DatabaseImageManifestTests`; expect all embedded pins and Doka versions to
  match, registry-port tag extraction to preserve the version, and malformed,
  mutable, missing, duplicate, and unknown stages to fail.
- On a disposable PostgreSQL developer volume, supply an incorrect password
  override after initialization; expect `up --wait` to fail. Restore the correct
  password and expect authenticated readiness and the original data to survive.
- Run selected live provider and ordinary migration tests plus benchmark contract
  tests; expect the existing independent resource lifetimes to work.
- Verify the SQL Server 2022 CU27 tag and digest in the shared manifest and fresh
  embedded consumers. On native Linux x64 CI, run every applicable SQL Server
  provider, ordinary migration, and SafeMigrations case with zero skips. Local
  ARM execution must keep database cases visible as skips before SQL Server
  startup while metadata and unit cases run. Unsupported CI must fail. Neither
  ARM candidate results nor Compose readiness satisfy native CI qualification.
- Inspect image-source paths and the ADR corpus separately; expect no stale
  image-source path, duplicate pin source, invalid local link, or changed Git index.

## Pros and Cons of the Options

### Direct Testcontainers plus optional Compose using shared Docker build stages

- Good, because existing test-owned resources and developer persistence remain independent.
- Good, because actual Compose build targets retain a single Dependabot-readable pin source.
- Bad, because the developer path needs Compose and a local FROM-only build step.

### Move automated fixtures to Testcontainers Compose

- Good, because Testcontainers can own a complete multi-service environment.
- Bad, because these suites need individual database engines rather than a
  coupled service topology; changing fixture orchestration adds no required capability.

### Keep direct Testcontainers without a developer Compose environment

- Good, because automated ownership needs no additional developer resource lifecycle.
- Bad, because manual Rider inspection would still require independently managed
  servers and image versions, leaving the requested developer workflow unresolved.

## More Information

The [Docker guide](../../docker/README.md) owns commands, connection settings,
and volume reset. [D-013](D-013-provider-owned-integration-test-projects.md)
owns provider suite organization; [D-015](D-015-benchmarkdotnet-development-measurements.md)
owns optional measurement boundaries. This decision introduces no CI/RC
workflow and no benchmark acceptance criterion.

### Re-evaluation Triggers

- Tests require a coupled multi-service topology that cannot be owned by an individual engine fixture.
- Dependabot gains external .env image resolution and a proposed pin-source
  change demonstrates equivalent update coverage without another parser.
- A pinned vendor image changes its readiness executable, data directory, or platform support.
- The vendor resolves the matching CU9 LSASS startup report, or another engine
  baseline is proposed; review the diagnosis and qualify the proposed shared
  digest on native Linux x64 before claiming a correction. An alternate Ubuntu
  base or a successful emulated rerun alone is insufficient.
- Rider displays hidden embedded resources despite Visible=false; inspect the
  evaluated project model before changing source/resource ownership.
- A proposal attaches automated consumers to persistent developer services or
  introduces cross-run reuse; isolation and failure cleanup require explicit review.

### Decision History

- 2026-09-29: Decision recorded with status proposed.
- 2026-09-29: Status changed from proposed to accepted.
- 2026-09-29: The owner requested the direct-Testcontainers and optional-Compose approach and specified Config/docker as the Rider location.
- 2026-09-29: Status changed from accepted to implemented.
- 2026-09-29: Confirmed all four native Compose profiles, persisted SQL data after stop/restart, and owned reset. Incorrect PostgreSQL credentials fail readiness on a reused volume; correct credentials recover the data.
- 2026-09-29: Confirmed fresh Release consumers with zero warnings/errors, 546 unit and 228 benchmark contract tests, five provider and five ordinary migration smoke cases, and one untimed MariaDB benchmark diagnostic operation. Rider layout is verified through solution structure and evaluated resource visibility, without claiming a UI readback.
- 2026-10-08: Selected the official CU9 Ubuntu 22.04 base after a reported LSASS startup crash on the CU9 Ubuntu 24.04 base matched the open vendor report. Both bases passed 36 local starts; no local reproduction or vendor root-cause confirmation is claimed. Added the explicit image re-evaluation trigger.
- 2026-10-08: A subsequent Rider report reproduced the identical startup crash on Ubuntu 22.04. The vendor report's follow-up comment confirms that base is also affected. Reverted the unproven image switch, retained the original Ubuntu 24.04 pin, and kept the unresolved startup failure explicit.
- 2026-10-09: The owner approved replacing the shared SQL Server 2025 CU9 pin with 2022 CU27 on Ubuntu 22.04 at the digest recorded above. The prior private candidate passed 1,072 provider, 10 ordinary migration, and 51 SafeMigrations SQL Server cases plus 30 starts under macOS ARM emulation. This is limited local candidate evidence, not native CI qualification or a confirmed CU9 fix.
- 2026-10-09: The owner also approved visible local ARM database skips before container startup under D-013. Metadata and unit cases remain runnable; native Linux x64 CI must execute all applicable SQL Server cases, and unsupported CI must fail. No new host, project, dependency, timeout increase, or global concurrency cap is introduced. Native CI evidence remains required.

### Implementation References

- [Canonical image source](../../docker/database-images.Dockerfile)
- [Developer Compose profiles](../../docker/compose.yml)
- [Operating guide](../../docker/README.md)
- [Embedded image reader](../../tests/Shared/DatabaseTestTargets.cs)
- [Manifest regression contracts](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DatabaseImageManifestTests.cs)
- [Solution configuration](../../Doka.EntityFrameworkCore.NestedSet.slnx)
- [Dependency updates](../../.github/dependabot.yml)

### Sources

- [Compose build specification](https://docs.docker.com/reference/compose-file/build/) (primary source; retrieved 2026-09-29)
- [Compose profiles](https://docs.docker.com/compose/how-tos/profiles/) (primary source; retrieved 2026-09-29)
- [Compose up and readiness](https://docs.docker.com/reference/cli/docker/compose/up/) (primary source; retrieved 2026-09-29)
- [Compose down and volume removal](https://docs.docker.com/reference/cli/docker/compose/down/) (primary source; retrieved 2026-09-29)
- [Compose service networking](https://docs.docker.com/compose/how-tos/networking/) (primary source; retrieved 2026-09-29)
- [PostgreSQL image authentication and initialization](https://hub.docker.com/_/postgres/) (primary source; retrieved 2026-09-29)
- [BuildKit stage dependency processing](https://docs.docker.com/build/building/multi-stage/#differences-between-legacy-builder-and-buildkit) (primary source; retrieved 2026-09-29)
- [BuildKit FROM-only integration test](https://github.com/moby/buildkit/blob/master/frontend/dockerfile/dockerfile_image_test.go) (primary source; retrieved 2026-09-29)
- [Dependabot Docker file discovery](https://github.com/dependabot/dependabot-core/blob/main/docker/lib/dependabot/docker/file_fetcher.rb) (primary source; retrieved 2026-09-29)
- [Dependabot Dockerfile parsing](https://github.com/dependabot/dependabot-core/blob/main/docker/lib/dependabot/docker/file_parser.rb) (primary source; retrieved 2026-09-29)
- [Dependabot Compose parsing](https://github.com/dependabot/dependabot-core/blob/main/docker/lib/dependabot/docker_compose/file_parser.rb) (primary source; retrieved 2026-09-29)
- [Testcontainers resource ownership practices](https://dotnet.testcontainers.org/api/best_practices/) (primary source; retrieved 2026-09-29)
- [Testcontainers Compose support](https://dotnet.testcontainers.org/api/compose/) (primary source; retrieved 2026-09-29)
- [EmbeddedResource visibility metadata](https://learn.microsoft.com/en-us/visualstudio/msbuild/common-msbuild-project-items?view=visualstudio) (primary source; retrieved 2026-09-29)
- [SQL Server container support](https://learn.microsoft.com/en-us/sql/linux/containers/deploy?view=sql-server-ver17) (primary source; retrieved 2026-09-29)
- [SQL Server container platform boundary](https://learn.microsoft.com/en-us/sql/linux/containers/deploy?view=sql-server-ver17) (primary source; retrieved 2026-10-09)
- [SQL Server 2022 CU27 build inventory](https://support.microsoft.com/en-us/servicing/sql/sql-server-2022/kb5023451-build-versions) (primary source; retrieved 2026-10-09)
- [CU9 native GitHub-hosted startup crash report; vendor diagnosis remains open](https://github.com/microsoft/mssql-docker/issues/974) (primary source; retrieved 2026-10-09)
- [CU9 intermittent startup crash report; vendor diagnosis remains open](https://github.com/microsoft/mssql-docker/issues/974) (primary source; retrieved 2026-10-08)
- [Reporter confirmation on Ubuntu 22.04; no vendor correction is claimed](https://github.com/microsoft/mssql-docker/issues/974#issuecomment-6036254848) (primary source; retrieved 2026-10-08)
- [SQL Server 2025 supported Linux distributions](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/setup?view=sql-server-ver17) (primary source; retrieved 2026-10-08)
