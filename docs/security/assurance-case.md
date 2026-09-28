# Security assurance case

This assurance case links the objectives in
[Security design](security-design.md) to repository controls and evidence. It
does not claim an external audit, organizational continuity, or successful
publication before those events exist.

## Case 1: Scope and TreeId isolation are structural

**Claim.** A facade cannot read or mutate a hierarchy node outside its selected
Scope and TreeId through the documented API.

**Argument.** TreeId is required model metadata. Scope is optional model
metadata and must be bound when configured. Anchor, range, parent, sibling,
validation, repair, and lock expressions include the complete identity. Bounds
may deliberately repeat across TreeIds and Scopes, so correctness cannot rely
on globally unique coordinates.

**Evidence.** Runtime query and mutation tests cover repeated bounds, wrong-
Scope keys, cross-Scope parents, several TreeIds in one Scope, arbitrary
descendant anchors, and provider-native identity equality. Index conventions
define Scope-optional, TreeId-leading structural paths and typed registry
keys; physical migration lifecycle validation belongs to the separate
migration-test change.

**Limitation.** The application authorizes which Scope and TreeId the caller
may select.

## Case 2: Structural mutations are atomic and serialized

**Claim.** A completed library mutation leaves valid tree state, and concurrent
library writers cannot interleave structural range changes within the same
complete tree identity.

**Argument.** One transaction or caller savepoint contains every range,
adjacency, registry, payload, and refresh step. The typed registry row for
`(Scope?, TreeId)` is locked before structural reads and writes. Multi-tree
requests are ordered by database identity semantics. Definite failures roll
back; commit-unknown outcomes are exposed for reconciliation rather than
retried.

**Evidence.** Provider integration tests inject failures after intermediate
updates, verify rollback and savepoint behavior, coordinate concurrent writers
without sleeps, check registry SQL and affected rows, and validate final trees.

**Limitation.** Writers outside this protocol can violate the claim.

## Case 3: SQL inputs remain data

**Claim.** Runtime scope, key, ordering, and payload values do not become SQL
syntax through NestedSet.

**Argument.** Values are EF expression parameters. Physical identifiers come
from finalized relational metadata and provider quoting. Fluent mapping accepts
direct property expressions rather than identifier strings.

**Evidence.** Query tests inspect parameterization and compare native binary,
string, numeric, and generated key behavior across providers. Physical-index
specifications define the expected catalogs; migration lifecycle evidence
belongs to the separate migration-test change.

**Limitation.** Application raw SQL and database objects outside the EF model
need separate review.

## Case 4: Failure behavior preserves evidence

**Claim.** Expected failures are classifiable without parsing text, and cleanup
does not conceal the initiating error.

**Argument.** `NestedSetException` carries a stable error code. Validation uses
typed issue codes. Cancellation remains cancellation. Cleanup uses an
appropriate non-canceled boundary and preserves dual operation/restoration
failures. Unknown commit outcomes are documented as unknown.

**Evidence.** Negative tests cover missing nodes/anchors, cycles, invalid
mapping/context/transactions/imports, cancellation before and during work,
rollback failure, callback interference, and post-commit ambiguity.

## Case 5: Diagnostics do not export application data

**Claim.** Library-owned metrics and activities have bounded cardinality and do
not disclose hierarchy or domain identifiers.

**Argument.** Instrument tags are constructed only from constant operation,
outcome, provider-family, and error vocabularies. Numeric measurements contain
only duration, lock wait, affected rows, batch count, and rebuild node count.
The telemetry layer does not inspect operation results or exception messages.

**Evidence.** Diagnostic tests subscribe to activities and meters for success,
failure, cancellation, and lock paths and reject NodeKey, Scope, TreeId, entity
or table names, SQL, connection data, messages, and payload.

**Limitation.** Application and EF/provider telemetry have their own privacy
contracts.

## Case 6: TreeId reuse is an explicit administrative action

**Claim.** Ordinary deletion cannot silently make a TreeId available again.

**Argument.** Complete-tree deletion atomically leaves a typed tombstone.
`PurgeTreeIdAsync` separately locks that tombstone, verifies that no filtered or
hidden hierarchy node remains, and deletes exactly one registry row. Missing,
active, unsupported, and damaged lifecycle states are rejected.

**Evidence.** Lifecycle tests cover deletion without reuse, successful purge,
deliberate reuse, active and missing identities, and a damaged tombstone that
still owns nodes.

**Limitation.** The application must separately authorize purge and retain any
business audit or external-reference evidence required before identity reuse.

## Re-evaluation

For each security-relevant change:

1. identify the affected claim;
2. update its argument and explicit limitations;
3. link concrete positive and negative evidence;
4. record a decision when ownership or trust boundaries change; and
5. remove a claim if its supporting control no longer exists.

A valid threat model and assurance case prepare an OpenSSF criterion; they do
not establish that an independent human security review has occurred.
