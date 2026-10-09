# Typed runtime regression coverage

This document maps the typed runtime's important failure mechanisms to
concrete tests. It helps maintainers select relevant regressions when changing
dispatch, identity, Parent, staging, locking, or tracker restoration. Feature
guides own public behavior; [D-012](decisions/D-012-typed-tree-runtime.md) owns
the architectural choice, and [implementation design](implementation-design.md)
explains the runtime path.

A positive case verifies a supported operation. A negative case verifies
rejection and preservation of inputs, persisted rows, or transaction state.
An adversarial case injects changes, cancellation, or failure at an awaited
boundary. Tests use one Arrange, Act, Assert sequence; setup may create a
hierarchy before the one operation being assessed.

## Mechanism matrix

| Risk | Positive coverage | Negative or adversarial coverage |
| --- | --- | --- |
| Incorrect generic roles | Configured ordinary and named mappings | Wrong roles or foreign mapping before SQL |
| Parent presence lost | Assigned default keys and absent detached roots | Direct Parent-to-null change rejected |
| Mapped property shape lost | Field, shadow, converted, compiled, shared models | Missing named mapping or capture metadata |
| Inheritance key misplaced | Root-owned scoped key and concrete TPC self-FK | Covered by valid-model finalization regression below |
| Concrete subtype narrows a managed save | Mixed TPH/TPT Parent, ordering, and tracked refresh | Missing Parent, cycle, and post-payload rollback |
| Converted key loses principal collation | Reference, value, explicit and implicit enum text mappings | Missing Parent, alias cycles, and late failure |
| Physical comparison lost | Source/registry parity and per-table TPC capture | Native aliases rejected and stale capture regenerated |
| Mutable identity ownership | Independent configured snapshots | Awaited mutation and callback changes |
| Tree isolation lost | Identical local bounds in distinct trees | Duplicate root, relative cross-tree placement |
| Tracker membership confused | Unaffected provider-distinct tree allowed | Native aliases and stale externally moved rows |
| Guard transport grows per node | Native collections and bounded fallback | Late affected membership or canceled probe |
| Application collection mode leaks | Explicit bounded scalar policy | Global Constant/Parameter modes preserve comparisons |
| Native CLR type hides conversion | Finalized mapped-property eligibility | Integer-to-text keys retain scalar fallback |
| Guard capture multiplies requests | Complete provider-equal pair hashing | Cross-pair false matches and broad domain equality |
| Callback rollback incomplete | Audit writes can be saved again | Later insertion batch and hierarchy failure |
| Bulk refresh retains detached inputs | Compiled mapped setters and bounded EF batches | Weak-reference checks with an unrelated tracked marker |
| Early failure overwrites untouched inputs | Per-batch generated capture | Never-staged non-sentinel generated values stay unchanged |
| Assigned-key callbacks replace rollback identity | Original independent key snapshots | Integer replacement and in-place binary mutation before/after save |
| Registry lifecycle weakened | Active/new/tombstoned request contracts | Foreign metadata and unavailable identities |
| Lock observation ineffective | Exact Scope/TreeId lock predicate | Two writers meet before acquiring their row |
| Managed Parent plan invalid | Dependencies execute before sources | Cycles, null Parent, payload rollback |
| Model cache leaks state | Same-model immutable metadata reuse | Different model and hierarchy separation |
| Query UX adds hidden reads | Composed anchor query uses one command | Missing or filtered anchor returns no rows |
| Structural work loses bounds | Existing statement/allocation limits | Wide, deep, and failed-operation regressions |
| Framework seams resolved too late | Complete registration-time insertion contract | Incompatible member/version diagnostic before context creation |
| Unchanged insertion keys allocate per boundary | Typed scalar/FK snapshot reuse | Sidecars, conceptual nulls, custom sentinels, mutable-key rejection |

The sections below name the source methods behind the matrix. The matrix is a
selection guide, not a replacement for the full provider suites.

## Dispatch and finalized mapping

Insertion framework binding is checked separately from database behavior.
`RequiredInsertionReflectionContractResolvesEveryMemberAndGenericShape` binds
all required native members, metadata setters, and scalar/composite/nullable
map specializations without populating the operation cache.
`ClosedMapOperationsAreCompiledOnlyWhenRequestedAndCached` verifies that
requested map operations compile lazily and are reused from that cache.
`RelationshipReadersUseCapturedMetadataAndInstalledSnapshot` verifies that
typed readers retain the installed composite-key snapshot independently of
changed current values. `IncompatibleFrameworkHasARegistrationDiagnostic`
requires the loaded version, exact missing member, and registration guidance.
`RegistrationValidatesWithoutCreatingAContext` exercises `UseNestedSets()`
without a provider or model. Structural compatibility does not qualify the
behavior of a future EF patch; its insertion and rollback matrix must still run.

Source: [insertion contract controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/InsertionReflectionContractTests.cs).

Positive:

- `NamedSharedStoreUsesTheSelectedMapping` verifies the selected named entity
  mapping rather than inferring it from the CLR type.
- `RootAndChildUseExactTreeIdentity` and `BulkImportsAssignExactTreeIdentity`
  exercise public facade dispatch into single and bulk features.

Negative:

- `TreeTypeMismatchIsRejectedBeforeDatabaseAccess`,
  `ForeignModelMetadataIsRejectedBeforeDatabaseAccess`, and
  `DifferentHierarchyMetadataIsRejectedAfterCacheWarmup` protect exact model
  and generic-role validation before database work.
- The binding tests reject incorrect Scope, NodeKey, and TreeId types and
  missing Scope binding through public queries and mutations.
- `ExecutorRejectsIncorrectGenericIdentityRoles` and
  `ExecutorRejectsForeignHierarchyRequests` protect the execution boundary.
- `WrongArgumentTypeRejectsBeforeSqlAndPreservesState` exercises public
  binding routes with mismatched generic roles and verifies zero commands,
  unchanged detached input, application tracker state, rows, and registry.
  Validate, rebuild planning, and rebuild probe their internal binding entries
  directly, so `InTree(...)` cannot reject first and mask a missing guard.
  Each move variant verifies its shared generic NodeKey role; it does not claim
  independent source/target scalar validation.

Sources: [typed store tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Storage/TypedTreeStoreTests.cs),
[binding tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Facade/NestedSetFacadeTests.Binding.cs),
[facade tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.cs),
[bulk tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.BulkAndMaintenance.cs),
[guard tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.cs),
and [dispatch tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/MutationDispatchContractTests.cs).

## Mixed inheritance saves and converted Parent comparison

The mixed TPH/TPT regressions change Parent across sibling derived types,
move dependent subtrees between trees, and rename a base ordering field with
both tracker acceptance modes. They inspect persisted and tracked descendants,
not only the concrete type that initiated the change. Missing Parent, cycle,
and injected post-payload failures must restore payload and pending tracker
state. Model-only owner tests distinguish cached base owners, independent
concrete TPC stores, and named shared mappings.

SQL Server derived-rowversion cases verify subtype-only generated tokens after
reordering, same/cross-tree Parent changes, and subsequent payload saves under
both acceptance modes. Base-facade single/bulk inserts must return the current
token; late faults must restore input and tracker values. The reader probe
rejects selection of ordinary Name/Payload columns during structural refresh.

Converted text-key cases use different principal and Parent collations on each
engine. Custom reference keys, custom value keys, explicit enum conversion,
implicit text-store conversion, and required nullable keys cover upward and
downward queries, move, delete, coordinated save, inspection, and rebuild.
Missing targets, alias cycles, and late faults protect rejection and rollback.
The structural projection case checks that only structural columns are read;
an absent nullable Parent must not materialize a converted default key from
SQL NULL or load the domain payload.

Sources:
[mixed managed-save cases](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/Inheritance/InheritanceTests.ManagedSave.cs),
[owner identity cases](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/HierarchyOwnerTests.cs),
[derived generated-token cases](../tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Ordering/Tracking/RowVersion/DerivedRowVersionTests.cs),
[converted principal comparisons](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/ValueConverters/ConvertedTextCollationTests.cs),
and [converter models](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/ValueConverters/ConvertedTextCollationNodes.cs).

The shared tracked refresh uses exact EF entry identity for generated tokens,
including derived, named, and complex mappings. Existing SQL Server rowversion
and callback-rollback cases verify pending payload originals, both acceptance
modes, and successful later saves; the refactor does not establish a separate
token policy.


## Parent presence and ownership

Positive:

- `InsertUnderDefaultKeyPreservesParentPresence` and
  `ReparentToDefaultKeyPreservesParentPresence` exercise assigned integer `0`
  and `Guid.Empty` keys. Presence must remain distinct from the default key.
  The same scenarios use a converted `IEquatable<TKey>` struct without `==`
  or `!=` operators; typed expression construction must still translate.
- `DetachFromDefaultKeyCreatesAbsentParent` checks the opposite state: a new
  independent root has no parent, while its descendant retains its parent.
- `CallbackMayReplaceParentWithProviderIdenticalValue` accepts a replacement
  Parent object with the same persisted representation for single and bulk
  insertion, both before and after payload persistence.

Negative and adversarial:

- `CallbackParentMutationRestoresConfiguredInput` mutates a configured mutable
  Parent during a SavingChanges or SavedChanges callback for single or bulk
  insertion. Rejection must restore the original detached structure rather
  than retaining a partially staged value.
- `ConvertedIdentityUsesConfiguredSnapshot` and
  `ShallowArrayComparerCannotShareStructuralSnapshotStorage` protect snapshot
  independence even with mutable converted values or a shallow comparer.
- `DetachmentOwnsConfiguredScopeNodeKeyAndTreeIdentityAcrossAwait`,
  `MoveSnapshotsBinarySourceAndDestinationKeys`, and
  `InsertSnapshotsBinaryParentKey` prevent caller mutation of identities while
  asynchronous discovery runs.
- `DeferredAnchorQueryOwnsConfiguredScopeAndNodeKey` and
  `DeferredTreeQueryOwnsConfiguredScopeAndTreeId` mutate caller identities
  after building deferred queries. Execution must still select the original
  anchor, Scope, and tree through independent configured snapshots.
- `TreeValidationOwnsConfiguredScopeAndTreeId` and
  `RebuildPlanOwnsConfiguredScopeAndTreeId` change caller identities after
  binding a tree facade. Maintenance must use the same original TreeId as its
  query while preserving rows, registry state, and an empty tracker.

Sources: [parent tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/TypedParentContractTests.cs),
[mutable tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/ConfiguredMutableIdentityTests.cs),
[deferred query ownership](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/ConfiguredMutableIdentityTests.Queries.cs),
[binary tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/MutableTreeIdentityTests.cs),
and [snapshot tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/ConfiguredIdentitySnapshotTests.cs).

## Explicit bounded collection policy

Positive:

- `NativeMappedBatchUsesBoundedPredicate` verifies empty, singleton, two-key,
  and 64-key batches against a finalized native mapping.
  `ScalarEqualityUsesOneMappedComparison` keeps an explicitly scalar consumer
  independent from collection eligibility.
- `ConvertedNativeClrBatchRetainsScalarEquality` and
  `ConvertedStringBatchRetainsScalarEquality` inspect actual converters and
  generated scalar OR predicates. These unit controls cover integer-to-text
  and string-to-string mappings without opening a database connection;
  `NativeArrayBatchRetainsEqualityFallback` preserves the separate native
  array-key fallback.
- `CollectionModesPreserveParameterizedNativeMembership` exercises string
  and binary keys under global Constant and Parameter collection modes on
  all five engines. Actual scalar parameter values must remain present, and
  an existing requested key must be selected exactly.
- `SortedBulkRefreshRetainsParameterizedNativeKeys` uses those same key
  representations and global modes for sorted subtree import. It compares
  every imported CLR structure with persisted rows, checks unrelated tree
  preservation, and observes one refresh with the imported scalar bindings.
- `ConvertedSortedBulkRefreshUsesScalarBindingsAndPreservesOtherTrees`
  imports sorted nodes whose integer model keys are converted to text under
  both global modes. The actual refresh must retain mapped scalar bindings
  and OR equality while imported CLR geometry matches persisted rows and
  neighboring trees remain unchanged.

Negative:

- `MismatchedMappedTypeIsRejected` supplies long values to an integer mapped
  property. It must reject the generic mismatch before SQL execution.
- The missing-key cases of
  `CollectionModesPreserveParameterizedNativeMembership` return no rows while
  retaining scalar bindings. Correct result counts alone cannot establish
  the library's per-query policy: literals or a serialized collection must
  not silently replace the intended parameters.
- `SortedBulkRefreshIdentityEditRestoresInputsAndPersistedTrees` edits a
  string identity or binary identity contents at the refresh reader boundary
  after sorted import writes. Both global modes must reject the late edit and
  restore every imported input and persisted tree, leaving the tracker empty.
- `ConvertedSortedBulkLateIdentityEditRestoresInputsAndAllTrees` changes an
  imported converted key at that same late boundary. Rejection must restore
  the original input identities, geometry, payload, and every persisted tree
  while the observed refresh retains the same scalar converter policy.

Sources: [collection overrides](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Indexes/KeyFilterComparisonTests.CollectionModes.cs),
[mapped collection policy](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Storage/NestedSetKeyFilterTests.cs),
[sorted bulk refresh](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering/Keys/OrderingKeyTests.BulkCollectionModes.cs),
and [converted sorted imports](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering/Keys/OrderingKeyTests.BulkConvertedKeys.cs).

## Live typed current-value reads

Positive and adversarial supported reads:

- `IntegerParentReadsLiveNullablePresence` and
  `GuidParentReadsLiveNullablePresence` distinguish absence from a present
  default key after capturing the live values wrapper.
- `ReferenceParentReadsLivePresence` and
  `ConvertedReferencePresenceIgnoresDomainOperators` preserve reference null
  semantics even when a converted domain type supplies misleading equality
  operators. An empty string remains a present parent.
- `OperatorFreeStructReadsLiveNullablePresence` accepts a nullable converted
  struct without equality operators, while
  `RequiredNullableClrKeyDoesNotNestNullableStorage` retains the existing
  nullable CLR shape of a required EF key.
- `ShadowParentReadsLiveSidecarChanges` and
  `NamedSharedParentReadsLiveIndexerChanges` observe updated shadow and named
  dictionary storage through the wrapper captured before the change.
- `TemporaryIdentityRemainsLiveOutsideClrStorage` creates a generated
  temporary key after capturing current values. The typed getter must read
  the sidecar key while the entity's CLR key remains zero.
- `RepeatedParentAndStructuralReadsAvoidPerReadAllocation` warms one wrapper
  and cached adapters, then executes 100,000 integer/converted Parent and
  geometry reads with an observable sum. The fixed fixture's total allocation
  is bounded by 4,096 bytes; this is not a universal EF allocation guarantee.

Sources: [live typed reads](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Storage/TypedPropertyReadTests.cs)
and [exact model fixtures](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Storage/TypedPropertyReadTests.Models.cs).
The late identity-change and attachment rejections under native tracked
membership remain separate controls; correct live reads do not replace the
guard's independently owned snapshots or its original entry-state checks.

## Supported EF mapping shapes

Positive:

- `ShadowStructureSupportsInsertAndQueryAsync`,
  `NamedSharedTypeSupportsInsertAndQueryAsync`, and
  `CompatibleStrongIdConvertersSupportHierarchyOperationsAsync` retain the
  existing shadow, named shared, and converted model contracts.
- `NamedSharedClrSaveRefreshesSortedSiblingsAndKeepsOtherMapping` performs one
  coordinated sibling reorder on a named shared CLR mapping while saving an
  unrelated entity under another name for that same CLR type.
- `NamedPropertyBagParentSaveKeepsExactMetadataAndOtherMapping` moves a named
  property-bag child across trees in one normal save. Refreshed structure,
  original values, and entry metadata must remain owned by the selected
  hierarchy while the unrelated named bag write is preserved.
- The configured field-access tests cover insert, child insert, move,
  validation, and rebuild without relying on public property setters.
- `CompiledRegistryNamesMatchDesignModel` and
  `NumericHierarchyUsesTheSameGeneratedModel` verify finalized and generated
  metadata consumers rather than rebuilding a hidden alternate model.
- `RequiredNullableClrKeySupportsRootInsertion` and
  `RequiredNullableClrKeySupportsParentJoinDuringRebuild` preserve accepted
  EF metadata whose NodeKey column is required but whose CLR type is `int?`.
  Parent bridges must reuse its nullable representation rather than construct
  an invalid nested Nullable type.
- `ScopedConcreteParentKeyBelongsToInheritanceRoot` runs on all five engines.
  It finalizes a valid scoped TPC model with a scalar primary key and checks
  that the `(Scope, NodeKey)` alternate key belongs to the inheritance root.
  Each concrete self-FK retains that key, its concrete principal entity type,
  `(Scope, Parent)` dependent properties, and Restrict deletion. The scalar
  primary key remains unchanged; the regression prevents invalid derived-key
  creation without changing the valid model to avoid the failure.

Negative:

- `SharedTypeWithoutEntityNameIsRejected` requires the exact named mapping.
- `SuppliedModelWithoutCollationCaptureHasAnActionableDiagnostic` rejects a
  supplied model without the native comparison metadata it needs.

Sources: [shadow tests](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/ModelCompatibility/ShadowProperties/ShadowPropertyTests.cs),
[shared types](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/SharedTypes/SharedTypeTests.cs),
[named shared managed saves](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/SharedTypes/SharedTypeManagedSaveTests.cs),
[converted keys](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/ModelCompatibility/ValueConverters/StrongIdTests.cs),
[required nullable CLR keys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/ValueConverters/RequiredNullableKeyModelTests.cs),
[scoped inheritance keys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/Inheritance/TpcSharedCollationTests.cs),
[field access](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Contracts/ContractTests.FieldAccess.cs),
and [compiled models](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/CompiledModels/CompiledModelTests.cs).

Common methods run on each concrete engine unless a method or variant has a
reasoned exclusion. Local model facts retain their explicit provider owner.
Naming a test here does not override either eligibility rule.
The required nullable-CLR-key rebuild regression seeds its branch through
public forest import with a Guid TreeId. Setup needs no scalar-key generic
argument or reflection. These tests qualify the model and runtime bridge;
public scalar-key APIs retain their `notnull` annotation.

## Effective physical collation and registry parity

The registry and native rowset tests below run on MySQL and MariaDB. They
distinguish known physical-table metadata from an unknown database default;
neither broad CLR equality nor a property-only capture establishes native
identity.

Positive:

- `InheritedBinaryScopesCanReserveTheSameTreeIdIndependently` exercises public
  insertion with case-distinct binary Scopes and one TreeId. Each Scope keeps
  its own root with local bounds and the first root remains unchanged.
- `InheritedBinaryScopesAcquireBothLocksInCanonicalOrder` observes actual
  registry lock Scope bindings for both request orders. Both distinct
  identities must be locked in the same database order without changing rows.
- `ExplicitScopeCollationDeduplicatesExistingAliasLocks` verifies that an
  explicit case-insensitive column collation overrides the binary table
  default and acquires one lock for equivalent existing identities.
- `SharedPropertiesRetainConcreteTableCollations` proves that the same
  inherited NodeKey, Scope, and Parent property objects retain different
  effective facets for their binary and case-insensitive TPC tables.
- `BinaryConcreteTableCanReserveIndependentScopes` preserves the public
  same-TreeId reservation contract on the binary concrete TPC table.
- `LeafTableCollationPreservesIndependentAncestorScopes` checks a TPT
  hierarchy whose structural base table and leaf payload table differ.
  The leaf's known canonical binary declaration must reach the mapped
  ancestor store, registry, and captures; public insertion preserves both
  case-distinct Scopes sharing one TreeId, including base and leaf payload.
- `ConcreteLeafFacadeUsesConfiguredAncestorCaptures` checks parent queries
  through the concrete TPT leaf facade against its configured ancestor's
  physical comparison facts.
- `ConcreteLeafParentSaveUsesConfiguredAncestorCaptures` applies a normal
  concrete-leaf Parent, Name, and payload save. The configured ancestor
  capture must serve the coordinated move while geometry and leaf payload
  remain correct.
- `CaseInsensitiveConcreteParentAcceptsKeyAlias` checks that parent lookup and
  child structure use the concrete case-insensitive principal key semantics.
- `SecondaryFragmentReservesIndependentCaseDistinctScopes` uses string
  identities on a secondary entity-splitting structure fragment. Its known
  binary table default overrides the case-insensitive model default without
  explicit property collations. Case-distinct Scopes sharing one TreeId retain
  independent roots, complete payload, and matching registry comparison.
- `CompiledStringIdentityCapturesMatchDesignModel` compares generated and
  ordinary owner-level string captures on all five engines, including Doka's
  known table collation and a distinct explicit Parent collation.

Negative:

- `ExplicitScopeCollationRejectsDuplicateTreeReservation` rejects a second
  public root for a native Scope alias with TreeIdUnavailable and preserves
  detached input and the existing tree.
- `ExplicitScopeCollationRejectsDuplicateImportedTreeIdentities` rejects
  equivalent new identities in the native rowset check before reservation;
  no registry lock, node, or registry row may be created.
- `CaseInsensitiveConcreteTableRejectsScopeAliasReservation` rejects duplicate
  native Scope reservation on the case-insensitive TPC table while preserving
  the first root and detached input.
- `BinaryConcreteParentRejectsKeyAlias` rejects a case-distinct parent key
  with NodeNotFound. The other concrete table's case-insensitive facets must
  not leak into its lookup; input and root remain unchanged.
- `ConflictingAncestorOwnerCollationsRejectModelCapture` rejects contradictory
  applicable TPT owner declarations for the same physical column with a
  diagnostic naming the table and both values. This is NestedSet's deliberate
  ambiguity constraint; it does not assert that Doka rejects the declarations.
- `ConcreteLeafFacadeRejectsBinaryParentKeyAlias` checks leaf insertion with
  a case-distinct parent key. Binary ancestor comparison rejects the alias
  and preserves the detached child and persisted structure.
- `SecondaryFragmentRejectsBinaryParentAliasWithoutWrites` rejects a
  case-distinct parent with NodeNotFound using the secondary table's binary
  comparison rather than the model default.
- `SecondaryFragmentRejectsDuplicateReservationWithoutWrites` rejects an
  exact duplicate Scope/TreeId with TreeIdUnavailable. Both secondary-fragment
  negatives preserve detached input, complete payload and structure, registry,
  tracker, and transaction state.
- `SuppliedPropertyOnlyCaptureRequiresRegeneration` runs on all five engines
  and rejects a stale supplied model that has only the earlier property-level
  capture. Its diagnostic requires regeneration and the supplied model is
  retained rather than rebuilt or silently assigned another comparison.

Sources: [registry comparison tests](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/InheritedRegistryCollationTestBase.cs),
[concrete TPC facets](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/ModelCompatibility/Inheritance/TpcCollationTestBase.cs),
[TPT ancestor ownership](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/ModelCompatibility/Inheritance/TptInheritedCollationTestBase.cs),
[secondary entity-splitting structure](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/ModelCompatibility/TableMappings/EntitySplitCollationTestBase.cs),
and [generated and supplied models](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/CompiledModels/CompiledModelTests.cs).

## Independent tree geometry and lifecycle

Positive:

- `IndependentTreesKeepLocalBounds` and `TreeBindingIsolatesIdenticalBounds`
  prove that equal coordinates do not identify the same tree.
- `MoveToTransfersSubtreeAcrossTrees` and `DetachCreatesIndependentTree`
  preserve adjacency, Depth, and local bounds during tree changes.
- `PurgeRemovesTombstonedTreeIdentity` and `PurgedTreeIdCanBeReused` verify the
  separate administrative lifecycle after deliberate retirement.
- `BinaryRequestRetainsIndependentTypedSnapshots` and
  `ScopelessRequestUsesMarkerWithoutInventingAStoredScope` verify the typed
  request and its narrow heterogeneous view.

Negative:

- `DuplicateRootIsRejectedBeforeNodeWrite`,
  `ForestRejectsDuplicateTreeIdentityAtomically`, and
  `TreeMaintenanceRejectsMultipleRoots` prevent a second root in one TreeId.
- `RelativeMoveRejectsAnotherTree`, `DeletedTreeIdCannotBeReused`, and
  `DetachRejectsUnavailableTreeId` preserve explicit placement and lifecycle
  restrictions.
- `DetachRejectsActiveDestinationWithoutChangingEitherTree` checks both
  geometries and registry revisions when the requested target is already
  active; an active identity cannot be adopted as a new detached tree.
- `ActiveTreeIdCannotBePurged`, `UnknownTreeIdCannotBePurged`, and
  `TombstoneWithNodesCannotBePurged` reject unsafe administrative reuse.
- `ReadBoundaryRejectsLifecycleTransitions` prevents inspection from acquiring
  a request that creates or retires a tree identity.
- `GenericRoleMismatchIsRejectedBeforeDatabaseAccess`,
  `ScopelessRequestRejectsAnUnrelatedGenericScopeRole`, and
  `SameModelForeignNamedHierarchyIsRejectedBeforeDatabaseAccess` reject
  incorrect roles or another named mapping even within the same model.

Sources: [tree tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.cs),
[tree queries](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Facade/NestedSetFacadeTests.Queries.cs),
[bulk tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.BulkAndMaintenance.cs),
[failures](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Failures.cs),
[active destination](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Invariants.cs),
[purge tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Purge.cs),
[read boundary](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TypedTreeReadBoundaryTests.cs),
and [request tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TypedTreeLockRequestTests.cs).

## Native tracked membership

Positive:

- `MutationAllowsTrackedEntityFromUnaffectedTree` permits unrelated tracking.
- `BroadModelComparerAllowsAnUnaffectedProviderDistinctTree` rejects the
  assumption that broad model equality proves native stored-tree equality.
- `NativeScalarTypesPreserveCollectionEqualityAtEveryBatchSize` qualifies
  accepted collection transport at scalar limits and batch boundaries.
- `NativeKeysUseOneProbeForTwentyThousandUnrelatedCandidates` requires one
  native membership command for 20,000 unchanged integer or Guid candidates.
  Its additional allocation is capped at 2,600 bytes per candidate after warm
  baseline subtraction, covering EF guard work and provider transport.
  [Performance measurements](performance.md) record the dated provider values
  and distinguish allocation from retained heap or CPU speed.
- `NativeKeysProbeEachSixtyFourTreeRequestBatchOnce` checks 65 complete
  requested tree identities against the same 20,000-candidate collection;
  the native path uses two request batches rather than candidate batches.
- `NativeKeyCollectionKeepsDuplicateKeysInAnotherScopeSeparate` preserves an
  unrelated Scope containing the same NodeKeys and TreeId. Transport must not
  form a Scope/key cross product.
- `NativeScopeAliasesKeepBothProviderRepresentationGroups` retains two
  captured representations of a native Scope alias. Both groups use a native
  SQL Scope predicate, so extra probes preserve complete membership.
- `BroadScopeEqualityKeepsBothProviderDistinctKeyGroups` keeps distinct stored
  Scope groups even when their configured domain values compare equal.
- `ProviderDistinctScopeAliasKeepsUnaffectedTrackedDuplicateKey` uses the same
  NodeKey and TreeId in two database-distinct case variants of Scope despite
  broader domain equality. Native and converted-key cases must allow the
  unaffected tracked row in one probe and preserve both trees and the tracker.
- `InheritedBinaryScopeCollationKeepsUnaffectedDuplicateKey` checks MySQL and
  MariaDB with native and converted keys when Scope inherits a binary table
  collation recorded in canonical metadata. The effective capture must retain
  that known value despite absent property/model collations. One native probe
  retains the function comparison, allows the operation, and preserves both
  trees and the tracker.
- `InheritedNoPadScopeCollationKeepsUnaffectedTrailingSpaceKey` checks MySQL's
  known inherited NO PAD collation with native and converted keys. Its
  effective table capture preserves distinct trailing-space Scopes; one
  native probe allows the operation and preserves both trees and the tracker.
- `ConvertedKeysUseOneNativeProbeForSixtyFiveCandidates` and
  `BinaryKeysUseOneNativeProbeForSixtyFiveCandidates` require one bounded
  scalar membership probe for 65 candidates rather than two 64-key probes.
- `CrossedRequestScopeAndTreeIdDoNotFormAnAffectedPair` permits an unaffected
  tracked complete pair whose Scope and TreeId occur in separate requests.
  Broad domain Scope equality must not create a false definite match.
- `CaptureProviderWorkScalesLinearly` measures configured converter calls
  while capturing and verifying 20,000 candidates against 65 requests. The
  bound grows with their sum and the database connection stays closed;
  this is comparer-work evidence rather than a CPU-time claim.
- `VerifyAcceptsEquivalentBinaryReplacement` replaces each captured Key,
  Scope, or TreeId with an independent provider-identical array. The retained
  live values wrapper must still observe that replacement without changing state.
- `LastEntryReplacementRemainsValidAcrossCandidateBlocks` checks 63, 64, and
  65 entries. A provider-identical replacement on the last entry remains
  accepted on both sides of the private buffer's block boundary.
- `ScalarCandidatesRespectBothBatchBoundaries` checks native string and
  explicitly converted keys on all five engines. Its 20,000-candidate case
  and combined candidate 769/request 65 case enforce the scalar-pack command
  formula and actual parameter ceiling, preserving tracker and persisted
  membership. Allocation output alone is not an asserted allocation budget.
- `TinyScalarScopesShareCombinedCandidateBatches` gives each of 768 or 769
  candidates its own provider-distinct Scope. Combined packs must use one or
  two probes with bounded parameters rather than one query per small Scope.
- `ScalarScopeDomainEqualityDoesNotRejectProviderDistinctCurrentIdentity`
  uses the requested TreeId with a domain-equal, provider-distinct Scope.
  The operation remains allowed in one scalar probe while both complete
  identities and the tracked row remain unchanged.

Negative and adversarial:

- `DefiniteAffectedIdentityRejectsWithoutDatabaseCommands` preserves immediate
  rejection for a definite affected provider representation.
- `ExactCompleteRequestPairRejectsBeforeSql` rejects the tracked exact
  Scope/TreeId pair before any command or operation delegate and preserves
  tracked values, rows, and transaction ownership.
- `VerifyRejectsInPlaceBinaryIdentityChange` mutates each captured identity
  despite a shallow model snapshot; late verification must reject it with
  InvalidContext. `VerifyRejectsDetachedCapturedEntry` rejects detachment even
  when the retained values wrapper still references the entry.
- `LastEntryEditIsRejectedAcrossCandidateBlocks` mutates the final TreeId
  in place at 63, 64, and 65 entries. Every case rejects with InvalidContext
  while preserving the preceding snapshots and application-owned tracker.
- `ScalarScopeDomainEqualityKeepsBothStaleCandidateGroups` places the same
  NodeKey in two provider-distinct Scopes that compare equal in the domain.
  Selecting the actual last tracker Scope must still reject stale membership
  in one combined scalar probe before the operation delegate, preserving both
  persisted groups and their tracked values.
- `NullRequiredTrackedTreeIdRejectsBeforeDatabaseCommands` attaches an
  unchanged entity with an invalid null required TreeId. It must receive the
  typed InvalidContext failure before SQL or transaction creation rather than
  reaching a converter or snapshot delegate with a null identity.
- `NativeScopeAliasCannotLeaveAffectedTrackedStructureStale` covers complete
  Scope/NodeKey pairs, including Scope-qualified EF keys.
- `NativePadSpaceScopeAliasRejectsAffectedStaleMembership` checks MySQL and
  MariaDB aliases differing by a trailing space. Their PAD SPACE collation
  must still identify the affected stored row: one native probe rejects with
  InvalidContext, runs no operation delegate, and preserves tracked state
  and persisted geometry.
- `UnknownDatabaseScopeAliasRejectsAffectedStaleMembership` checks MySQL and
  MariaDB with native and converted keys and no column, table, or model
  collation configured. An ordinary database lookup first establishes the
  real default's case alias. The column-native function then identifies the
  affected stale row in one probe, rejects before the operation delegate, and
  preserves rows, registry state, and tracker values without guessing a
  collation.
- `NativeTreeAliasCannotLeaveAffectedTrackedStructureStale` exercises converted
  native TreeId aliases; CLR equality alone cannot establish membership.
- `PersistedMembershipRejectsAnExternallyMovedTrackedNode` catches a stale
  tracked TreeId after another context moves the row into an affected tree.
- `NativeProbeCannotIntroduceUnplannedTrackerChanges` and
  `RegistryLockCannotAttachAnUnplannedAffectedHierarchyEntry` inject tracker
  changes during native reads or lock acquisition.
- `NativeKeysRejectAnAffectedStaleCandidateAfterTwentyThousandUnrelatedEntries`
  puts an affected stale identity after the large unrelated set; collection
  optimization cannot discard it or rely on its tracked TreeId.
- `SecondProviderDistinctScopeGroupRejectsAffectedStaleMembership` places the
  affected requested membership in the second provider-distinct Scope group,
  even though domain Scope equality equates both groups.
- `SecondTreeRequestBatchRejectsAffectedStaleMembership` places the affected
  stale row only in requested tree 65, after 20,000 unrelated candidates.
  Both second-group/batch cases require two probes, InvalidContext, no
  operation delegate execution, preserved persisted membership, and unchanged
  tracker state.
- `ScalarLastCandidateInLastRequestBatchRejectsStaleMembership` moves the
  actual last tracked leaf through another context into the final requested
  tree. Native string and converted scalar cases must reject before the
  delegate, obey both batch and parameter bounds, preserve the stale tracker,
  and leave the externally established database membership unchanged.
- `LastTinyScalarScopeRejectsStaleMembershipInSecondCandidateBatch` makes the
  actual final tracked Scope's TreeId stale. Persisted membership in candidate
  769 must reject in the second probe, with no delegate execution and unchanged
  tracked values, database rows, and transaction ownership.

Sources: [tracking tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Tracking.cs),
[guard tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.cs),
[Scope groups](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.Scopes.cs),
[inherited collation and padding](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/NativeCollationTestBase.cs),
[MySQL-only NO PAD guard](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/NativeCollationTests.cs),
[required tracked identity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.RequiredValues.cs),
[complete request pairs](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.RequestPairs.cs),
[packed scalar membership](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.ScalarCollections.cs),
[many small Scopes](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.CaptureWork.cs),
[typed capture and verification](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackedIdentityCaptureTests.cs),
[binary fallback](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.Binary.cs),
[transport tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Indexes/KeyFilterComparisonTests.NativeTypes.cs),
and [scale probes](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.Scale.cs).

## Callback writes and exact rollback

Positive:

- `PostSaveTrackerInspectionDoesNotRejectManagedInsert` permits ordinary
  callback observation of tracker state.
- `PostBaseTrackerInspectionDoesNotRejectManagedInsert` retains the SQLite-only
  save-override contract in the concrete `ManagedNestedSetDbContextCallbackTests`
  suite after separating it from the common callback source.
- `RolledBackCallbackWritesCanBeSavedAgain` verifies retry of callback Added,
  Modified, and Deleted writes after definite hierarchy rollback.
- `EquivalentSavedCallbackGeometryKeepsExactImportedStructure` assigns an
  unchanged Left, Right, Depth, or Position after the bulk payload save.
  The import remains valid and every imported value matches persisted geometry.

Negative and adversarial:

- `RolledBackManagedInsertRestoresAcceptedCallbackWrites` restores accepted
  callback state rather than retaining the failed insertion's save outcome.
- `LaterBatchFailureRestoresEarlierCallbackWrites` crosses the 64-node batch
  boundary with 65 nodes, then fails a later payload save.
- `SavedCallbackGeometryChangeRestoresInputsAndEveryPersistedTree` changes
  each managed geometry role after the payload write on all five engines.
  InvalidImport must restore the original input structure and all persisted
  rows, preserving neighboring trees, another Scope, and an empty tracker.
- `SuppressedManagedSaveRejectsInsertion` rejects a callback that suppresses
  the persistence assumed by the insertion plan.
- `CallbackCannotReplaceAssignedIntegerKey` and
  `CallbackCannotMutateAssignedBinaryKeyInPlace` reject assigned-key changes
  before and after payload save, restore exact original keys and structure,
  and leave no persisted import or pending hierarchy entry.
- `EarlyFailurePreservesUnstagedGeneratedInputs` checks both reservation
  failure and a failed first payload batch. Later inputs retain their own
  non-sentinel generated values instead of being reset to sentinels.
- `FailedStagingReleasesItsInputAndPreservesUnrelatedTracking` injects a
  one-shot generated getter or structural setter failure. The input is restored
  and collectible while the context and its unrelated tracked marker stay alive.
- `IdentityConflictReleasesInputWithoutRemovingExistingIdentity` exercises
  failed primary and alternate key installation. Cleanup releases only the
  introduced entry and preserves the legitimate colliding identity.
- `SingleIdentityCollisionPreservesLegitimateTrackedRowAndReleasesInput`
  preserves the original EF collision error, legitimate tracked row, and caller
  transaction state without reporting a false recovery failure. Weak references
  also verify release of the rejected input.
- `FinalSingleDetachmentFailureRollsBackGeneratedPayload` rejects the final
  detachment transition before commit. It restores generated root and owned
  values and pre-fixup assigned ownership keys. The separate
  `FinalSingleDetachmentFailureAllowsInputRetry` verifies persistence after an
  arranged failure, including the correct new ownership keys.
- `LateFailureRestoresOwnedDefaultsAndCollectionIdentities` fails after the
  second payload wave of a 65-root import. Generated defaults, public collection
  keys, and non-sentinel ownership FK/PK values restore; ordinary callback
  labels and business FKs remain caller-owned. Separate successful-import and
  retry cases preserve generated outputs and correct persisted ownership.
- `TrackingCallbacksReleaseOwnedInputsAndPreserveUnrelatedTracking` and
  `OwnedTrackingFailureReleasesTheCompletePartialGraph` reject initial root or
  owned transitions. Weak references detect retention invisible to public
  tracker enumeration; retry and ordinary caller payload remain usable.
- `ScalarKeyRejectionReleasesInstalledIdentity` and
  `MutableKeyRejectionReleasesRootAndOwnedIdentities` cover installed scalar,
  binary, and converted mutable identities, including aliased owned Scope
  keys. The positive `MutableIdentitiesRemainUsableAfterSuccessfulImport`
  checks normal persistence and later identity lookup.
- `AssignedScalarKeyMutationRollsBackSingleInsert`,
  `AssignedBinaryKeyMutationRollsBackSingleInsert`, and
  `AssignedCustomKeyMutationRollsBackSingleInsert` exercise before/after-save
  key edits, caller savepoint ownership, exact restoration, and empty failed
  identities. Separate `AssignedScalarKeyFailureAllowsSingleInsertRetry`,
  `AssignedBinaryKeyFailureAllowsSingleInsertRetry`, and
  `AssignedCustomKeyFailureAllowsSingleInsertRetry` arrange the rejected
  operation before the one successful retry being tested.
- `GeneratedSingleKeyMutationRollsBackInput` checks CLR key changes
  hidden by a generated-value sidecar. The positive
  `GeneratedSingleIdentitiesPersistAndCanBeFoundAfterDetach` retains ordinary
  generated identity behavior; `GeneratedSingleKeyFailureAllowsRetry` tests
  reuse after the arranged rejection.
- `FailedOrderingRestoresUnchangedCallbackPayload` and
  `UnchangedMutablePayloadRetainsItsOwnedRollbackSnapshot` restore mutable
  payload snapshots after a later structural failure.

Sources: [callback tests](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/SaveChanges/ManagedSaveCallbackTests.cs),
[SQLite save-override callbacks](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/SaveChanges/ManagedNestedSetDbContextCallbackTests.cs),
[bulk saved-geometry callbacks](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/BulkImport/BulkStageGuardTests.Geometry.cs),
[assigned-key callbacks](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/BulkImport/BulkStageGuardTests.AssignedKeys.cs),
[detached refresh and untouched inputs](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkDetachedRefreshTests.cs),
[early staging retention](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkStagingRetentionTests.cs),
[installed bulk identities](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkIdentityRetentionTests.cs),
[single assigned keys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.AssignedKeys.cs),
[single assigned-key retries](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.AssignedKeyRetry.cs),
[single generated and owned identities](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.NativeIdentity.cs),
[single generated and owned retries](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.NativeIdentityRetry.cs),
[single input collection](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.Retention.cs),
[ordering rollback](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering/Tracking/OrderingTrackerTests.SnapshotCallbacks.cs),
and [snapshot tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackerSnapshotAllocationTests.cs).

Sparse `OriginalStructure` values belong to this rollback contract. Their raw
null/sentinel representation is intentional; these snapshots do not replace
the typed active NodeKey, Scope, TreeId, or Parent used by an operation.

## Transactions, locks, and Parent planning

Positive:

- `ApplicationTransactionCommitIsAtomic` and
  `ApplicationTransactionRollbackIsAtomic` compose hierarchy and application
  writes with caller transaction ownership.
- `NativeProbeCanRetryAfterCancellationInTheSameCallerTransaction` uses a new
  token after a canceled probe without clearing the tracker or replacing the
  caller's transaction. Its earlier application write remains present.
- `ParentChangesApplyDependenciesBeforeTheirSources`,
  `ParentChangesFollowTargetAcrossTrees`, and
  `ParentChangesResolveNestedDependencyChain` apply coordinated dependencies
  under the complete locked tree plan.

Negative and adversarial:

- `ConcurrentMutationsPreserveTheForest` meets two server writers at their
  actual registry locking SELECT before either acquires the row. Its observer
  checks the Scope/TreeId primary key, predicates, and provider-mapped
  parameter values; reservation INSERT observation is insufficient for an
  existing tree.
- `OpposingCrossTreeMovesDoNotDeadlock` and
  `ConcurrentDuplicateRootsHaveOneWinner` exercise competing lock plans.
- `CyclicParentPlanRollsBackPayload`, `ParentChangeToNullRequiresExplicitDetach`,
  and `ParentChangeRollsBackWithPayloadAndCanRetry` protect rejected plans and
  retryable rollback state.
- `CanceledNativeProbePreservesCallerWritesAndCleanTracker` cancels at the
  awaited native membership boundary. The mutation rolls back to its own
  savepoint while preserving the caller's transaction, payload, and clean
  tracked state.

Sources: [transactions](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Transactions.cs),
[writer barrier](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/RelationalTests.ConcurrentMutations.cs),
[cross-tree writers](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Concurrency.cs),
[duplicate roots](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Failures.cs),
[Parent plans](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering/Operations/OrderingTests.SaveChanges.Parent.cs),
and [probe cancellation](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeTrackedIdentityGuardTests.Cancellation.cs).

The server two-writer barrier does not run on SQLite. SQLite acquires its
database writer exclusion at the transaction boundary, so waiting for both
writers to reach a later row-lock command would block the probe itself.
SQLite retains its transaction, mutation, and callback regressions.

## Metadata cache, query composition, and deterministic budgets

`UnchangedScalarRefreshReusesSnapshots` warms and budgets 10,000 refreshes of
integer, Guid, compound, generated, shadow, string, field, and indexer keys.
Ordinary immutable keys reuse native vectors without boxing current values.
Differing member/model types keep EF's sentinel-aware object getter and a
separate bounded allowance. Generated-sidecar, conceptual-null, custom-sentinel,
and changed-CLR controls verify semantics alongside the allocation assertions.
Mutable reference keys still take independent snapshots at every boundary;
the existing adversarial lifecycle and weak-reference cases remain required.

Source: [insertion refresh controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.cs).

`UnchangedIdentityComparisonsAvoidBoxes` budgets both `Matches` and
`PrepareDetach`, including every composite key component and additional
installed relationship keys. `IndependentRootCaptureHasABoundedInitialAllocation`
separately guards the initial handle against unused dependent-bucket lists.
The eager-list mutation control fails at 304 bytes per root against the
280-byte ceiling; ordinary warmed comparisons allocate zero bytes. The
property-bag allowances preserve EF's sentinel-aware object conversion.

Recovery controls reject differing generated sidecar and CLR values, preserve
accepted generated identities, reject changes to either composite component,
and guard custom comparers against null operands. An intermediate key installed
by `DetectChanges` is removed even when the current CLR key has returned to its
original value. Unrelated identity slots and dependent buckets survive cleanup.
A missing non-nullable property-bag key retains the framework getter's own
diagnostic rather than inventing a replacement identity.

The framework contract tests validate exact closed map signatures without
populating the operation cache, then check lazy compilation and reuse for both
map definitions and EF's actual `IReadOnlyList<object?>` composite-key shape.
An isolated production assembly proves that the public options-registration
call initializes its contract. Typed relationship readers retain installed
snapshots independently of changed current values; incompatible bindings name
the loaded framework version and required member before a context exists.

Sources: [comparison budgets](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Comparisons.cs),
[recovery semantics](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Recovery.cs),
and [registration and binding contracts](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/InsertionReflectionContractTests.cs).

- `ContextsSharingModelReuseMapping` and `DifferentModelsHaveIndependentMappings`
  check the mapping cache's model-lifetime reuse and separation. This is
  mapping-cache coverage.
- `InterleavedNamedBindingsKeepRowsContextsScopesAndRegistriesSeparate` uses
  one model and a shared CLR type with two named mappings and different key/tree/scope
  roles, then interleaves bindings across two contexts and scopes. Rows and
  registry lifecycle remain owned by their original mapping and bound Scope.
- `SameEntityNameAcrossDifferentModelsKeepsTypedMutationBindingsSeparate`
  reuses the same CLR type and entity name in another model with different
  identity roles and physical tables. Interleaved mutations must select that
  model's typed specialization and registry without changing the first model.
- `ComposedAnchorQueryUsesOneCommand`, `MissingAnchorReturnsEmptyQuery`, and
  `FilteredAnchorReturnsEmptyQuery` protect anchor UX and global-filter
  behavior without an extra entity load.
- `SingleInsertAllocationPerUnrelatedTrackedEntityStaysBounded`,
  `TwentyThousandEntitiesUseCompactScalarSnapshots`, and
  `WidePlanStaysWithinMemoryBudget` retain their existing allocation budgets.
- `NativeComparisonsDoNotAllocatePerPair` bounds warm allocation to 16,384
  bytes across 20,000 integer, Guid, or present nullable-parent comparison
  iterations. Per-pair value-type boxing would exceed this fixed allowance.
- `ConvertedComparisonsAllocateOnlyTheNecessaryConverterBoundary` uses an
  actual configured struct converter: distinct model values 17 and 27 both
  map to provider value 7, while 18 maps to 8. Equal and unequal comparisons
  must follow those provider representations, with equal hashes for aliases.
  Across 20,000 warmed iterations, typed equality may allocate at most the
  direct Matches baseline plus 16,384 bytes. The baseline includes the
  required converter boundary; this rejects redundant per-pair boxing without
  claiming that configured conversion allocates nothing.
- `InsertChildHasBoundedCommandCount` and the automatic move budget tests
  retain command, tree-lock, hierarchy-read, and affected-row bounds.

Sources: [metadata](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/MetadataCacheTests.cs),
[binding isolation](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/MutationBindingIsolationTests.cs),
[anchor queries](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Facade/NestedSetFacadeTests.Queries.cs),
[query filters](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Facade/NestedSetFacadeTests.FiltersAndTracking.cs),
[single insertion](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Insert/SingleInsertScaleTests.cs),
[tracker allocation](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackerSnapshotAllocationTests.cs),
[native comparer allocation](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TypedProviderComparerTests.cs),
[bulk allocation](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/BulkPlanAllocationTests.cs),
[insert commands](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeBudgetTests.cs),
and [move budgets](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Hierarchy/Movement/AutomaticMoveBudgetTests.cs).

## Execution and evidence limits

SQL Server database cases remain discoverable on local ARM and report a runtime
skip before container startup. The standard xUnit before-test hook reads the
concrete provider fixture or the migration row's named engine argument; SQL-only
SafeMigrations theories declare their fixed engine. Explicit database-independent
metadata classes and methods continue to execute, including compiled-model and
fixture-ownership checks. Unit regressions distinguish metadata from engine-only
fixtures, verify method-level opt-outs, and protect mixed-row selection from
engine-looking payload values. Native x64 follows the execution path; an
unsupported CI host fails rather than skipping. Container startup exceptions on
eligible hosts are never converted into skips.

Sources: [shared platform contract](../tests/Shared/SqlServerTestPlatform.cs),
[platform positive and negative tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/SqlServerTestPlatformTests.cs),
[fixture and metadata selection tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/EngineTestDiscoveryTests.Platform.cs),
[migration row selection tests](../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Infrastructure/DatabasePlatformTests.cs),
and [qualification limits](support-and-qualification.md#provider-and-engine-matrix).

Four provider projects reference the non-runnable specification library and
own concrete subclasses of every abstract common suite for every engine. The
MySql project has separate same-named MySQL and MariaDB suites with distinct
closed provider fixtures; their test bodies remain shared. Common methods obtain
immutable `Engine` from their injected fixture. Ordinary fact and theory data
contains only scenario arguments and runs on every concrete engine by default.
Reasoned method and variant exclusions retain specific unsupported cases.
Even an engine with all methods explicitly excluded retains the suite's concrete
structural owner. Each common method and scenario variant must retain more than
one executable provider owner after combining method and row exclusions;
MySQL and MariaDB count as one owner. Exclusive bodies and helpers live in the
owning project. Local neutral cases obtain their engine from the exact leaf
fixture. Family cases compile once in a MySql-local abstract suite with two
leaves, while MySQL-only NO PAD cases have only a MySQL declaration. All local
suites use the same fixture-owned contract; no `Provider*` annotation or legacy
row-filtering path remains.
The seven MySQL-family bases each have their own file, with explicit existing
named collections on concrete leaves. `ScopeAliasRowset` keeps class fixtures
without adding a named collection. All providers place wide registry-rowset
cases under `Concurrency/TreeRegistry`; SQL Server separates rowset locking
from Scope validation. Provider-local model/support namespaces and renamed
row-version and writer-timeout suites require fresh model and behavioral runs.
Entity short names and physical mappings remain qualification invariants.
Unit tests exercise metadata and planning contracts separately while referencing
the same reusable infrastructure. Migration projects qualify ordinary EF
migrations and optional SafeMigrations integration.
See [supported databases and qualification](support-and-qualification.md).

Source links below the matrix point to common assertions or their exclusive
provider owners. They do not imply that the specification library runs tests
or that every linked method applies to every provider. Assembly containers and
class or collection databases retain their existing lifetimes; see
[test project ownership](implementation-design.md#test-project-ownership).

The five executable-wide `ProviderAnnotationTests` metadata audits run once in
each provider project. Reasoned MariaDB exclusions remove exactly five duplicate
metadata cases; both MySQL-family wrappers and all owned engines remain audited.
The scenario comparison permits only those named duplicates to disappear and
preserves each moved behavioral method and variant under its new local class.

The infrastructure regressions cover ownership and fixture boundaries. Shared
checks combine method and row exclusions and reject single-executable variants.
Local checks inspect inherited abstract-family methods as well as leaf methods,
accept fixture-owned neutral payloads, preserve explicit `MemberType`, resolve
unset member sources through the leaf, and reject invalid inherited annotations
or empty sources. Engine-ownership checks reject unknown engines, foreign
assemblies, and missing current-assembly context. The immutable name, marker,
and executable catalog supplies owned-engine and expected-registration
inventories, including both MySQL-family engines in one executable. A valid
suite engine with a foreign caller-supplied audit assembly must still fail;
that independent check remains after removing the redundant unknown-engine
branch. Constructor checks inspect
the concrete leaf through
public xUnit metadata, including exact class, collection, and assembly fixtures,
built-ins, test-argument fallbacks, and parent-only fixture dependencies. They
reject assignable-only substitutions, unsupported required parameters, malformed
constructors, and incorrect nested collection inheritance without constructing
database resources. Common engine discovery must reject unjustified exclusions
and unexpected empty data, retain immediate and delayed enumeration, and avoid
an extra factory enumeration solely for filtering.

The complete model-collection registration guard accepts the actual expected
engine-bound sets and rejects missing fixtures, foreign engines, wrong
resources, restored unbound `ModelCompatibilityDatabase`, and unexpected
resources. It examines every `ICollectionFixture<>` registration without
initializing it, so an unrelated registration cannot disappear through a
recognized-fixture filter. Catalog and registration controls complement
the existing constructor, named-collection, and fixture-lifetime regressions.
Shared query-plan evidence is written by the independent `QueryPlanTestSupport`
helper from all six existing call paths; preserve output-directory selection,
file naming, double-newline formatting, and asynchronous cancellation behavior.

The complete local migration accounts for 17 former legacy suites, 35 methods,
and 57 engine/scenario variants. Every database behavior variant remains a
qualification requirement. The 64 retired unit cases tested the removed
annotation, selector-row, or discoverer mechanisms; they are accounted for
separately from database scenarios. Meaningful engine ownership, current-assembly
isolation, fixture lifetime, and collection contracts remain covered, alongside
the positive and negative neutral-case guards. Immediate and delayed discovery
must preserve the same behavior variants after removing selector arguments.

Sources: [local and shared ownership guards](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderTestContract.cs),
[ownership regressions](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderTestContractTests.cs),
[engine fixture contract](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderFixture.cs),
[engine discovery and data](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/EngineTestAttributes.cs),
[engine ownership catalog](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderEngineOwnership.cs),
[engine ownership regressions](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderEngineOwnershipTests.cs),
[complete collection registration guard](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderCollectionFixtureContract.cs),
[collection registration regressions](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderCollectionFixtureContractTests.cs),
[shared query-plan evidence](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Indexes/QueryPlanTestSupport.cs),
[constructor guard](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderFixtureContract.cs),
and [constructor regressions](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderFixtureContractTests.cs).

Run a fresh Release solution build before any `--no-build` test invocation.
Record the source revision or source hashes, dependencies, provider/image
configuration, build output, and per-project TRX results for that same source.
A test source, an old TRX file, or a command listed here is not evidence of a
new successful run. Test enumeration must also show that the expected provider
cases were discovered.

No finite matrix detects every future regression. These tests protect named
contracts and reproduced risks; a changed provider mapping, translation,
framework patch, transaction setting, or new operation still requires fresh
review and relevant positive/negative qualification. Statement and allocation
budgets do not certify arbitrary hardware throughput. The large-scale goals
in [performance](performance.md) remain qualification targets until measured
with matching source and a documented environment.
