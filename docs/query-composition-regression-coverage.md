# Query composition regression coverage

The [implementation source map](implementation-design.md#source-layout) locates
the focused query and consumer partials. Those file boundaries do not change
the contract IDs below or turn consumer policies into product behavior.

## Reading the evidence

- Unit tests verify pure request, selection, expression and pre-I/O rejection
  contracts once. They use the actual sample assemblies where applicable.
- Shared specification tests execute through provider-owned fixtures in the
  MySQL, MariaDB, PostgreSQL, SQLite and SQL Server suites. A source reference
  alone is not evidence that all five engines executed successfully.
- Capacity tests reuse persisted structural populations; actual consumer
  builders and bounded payload are additional assertions on that population.
- Exact command counts, tuple bags, parameters, tracking, cancellation and
  before/after snapshots matter more than a successful translation alone.

The table is a source-coverage map. Execution qualification requires its own
explicit source boundary. The historical record below retains the remaining
native SQL Server boundary without qualifying later changes.
Every listed source contract has a corresponding semantic test. Qualification
of the complete large-profile and native-provider matrix is a separate result.

## Contract-to-test map

| ID | Assertion | Named executable checks |
| --- | --- | --- |
| Q01 | Two anchors have independent expected totals. Root, hidden, and missing seeds cannot reappear downstream. | [MultipleAnchorsCorrelateThreeSourcesAndDateOnlyEdges](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Projections.cs)<br>[EmptyOrInvisibleSeedCannotBeResurrected](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| Q02 | Repeated requested IDs produce one anchor occurrence. Overlapping anchors retain their own contributions. | [RepeatedRequestedKeysDoNotDuplicateAnchorsOrCollapseOverlap](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Multiplicity.cs) |
| Q03 | A radius of zero includes only self. Strict and inclusive options for each leg have explicit adjacency expectations. | [DescendantRadiusDoesNotCompressInvisibleLevels](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs)<br>[SameDirectionWalksPreserveIntermediateMultiplicity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| Q04 | Equal local keys and coordinates remain isolated across scopes and independent trees. Unscoped hierarchies require no artificial scope. | [CompositePrimaryKeysKeepEqualNodeKeysAndBoundsIsolatedByScope](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Composite.cs)<br>[DirectUnscopedFacadeComposesWithoutATreeWrapper](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Unscoped.cs) |
| Q05 | All six operations use configured shadow properties and backing fields rather than misleading CLR members, including rejection cases. | [RenamedShadowRolesPreserveIdentityAndRejectMissingOwners](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Shadow.cs)<br>[BackingFieldRolesIgnoreDecoratedClrGetters](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Fields.cs) |
| Q06 | A MemberInit projection joined to facts translates. Constructed endpoints and an incorrect named mapping are rejected before any command executes. | [MemberInitAndIndependentPayloadRetainTheSelectedMapping](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[ConstructedEndpointIsRejectedBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[NamedMappingEndpointMismatchIsRejectedBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Named.cs) |
| Q07 | A closed incoming link removes the entire descendant branch. An incorrect parent or a skipped depth causes complete-path validation to reject the path. | [ClosedDirectEdgePrunesCompleteBranch](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs)<br>[CompletePathRequiresEveryActualDirectParentAtTheNextDepth](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.DirectEdges.cs) |
| Q08 | Self remains valid on an empty path. Null or hidden endpoints cannot reappear downstream. | [ClosedDirectEdgePrunesCompleteBranch](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs)<br>[MissingEndpointOnlyRemovesItsOwnOccurrences](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs)<br>[EmptyOrInvisibleSeedCannotBeResurrected](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| Q09 | A raw pair of visible endpoints can span a hidden intermediate node. Complete-path validation and Nearest reject that path. | [HiddenIntermediateIsNotSilentlySkippedByPathValidation](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs)<br>[NearestRejectsHiddenIntermediateEvenWhenItIsNotACandidate](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs) |
| Q15 | Owners may be the node itself or above the anchor. An endpoint from another bound tree is rejected. | [NearestSelfHasNoMatchingAncestorFallback](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs)<br>[ForeignTreeEndpointCannotAdoptMatchingLocalBounds](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs)<br>[NearestDoesNotInterpretApplicationPeriodsAsStructuralEdges](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs) |
| Q21 | Model tests exercise the principal key's captured collation for aliases, nullable converted parents, and binary key value equality in each operation. | [GeneratedModelUsesCapturedPrincipalCollationForParentAliases](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Compiled.cs)<br>[ConvertedOperatorFreeKeysPreserveNullableParentAndMissingOwnerSemantics](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Converted.cs)<br>[BinaryKeysRetainDatabaseValueEqualityInEveryComposer](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Binary.cs) |
| Q22 | A foreign context, an incorrect named mapping, and a CLR-only predicate fail before SQL executes. | [NestedForeignContextRootFailsBeforeEitherReader](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[NamedMappingEndpointMismatchIsRejectedBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Named.cs)<br>[NonTranslatablePredicateFailsBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs) |
| Q24 | Aggregate and list terminals propagate the caller's token. Cancellation cannot produce a successful result. | [CancellationBeforeExecutionDoesNotProduceAResult](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Async.cs)<br>[CancellationDuringReaderDoesNotReturnPartialSuccess](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Async.cs) |
| Q25 | The first row is read while an atomic Move or Rebuild transaction changes temporal data and facts. Results must match the exact before or after snapshot. | [MixedQueryReadsOneQualifiedSnapshotAcrossAtomicStructureAndPayloadChanges](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Snapshots.cs) |
| Q26 | Two scalar criterion values yield different expected results with identical SQL text and parameter names. The assertion does not cover list cardinality. | [ChangingScalarCriteriaKeepsCommandShapeAndBindings](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs) |
| Q27 | Existing query APIs compose through captured SelectMany sources, and the snapshot test covers concurrent Move and Rebuild. Existing mutation, SaveChanges, and bulk suites provide the separate compatibility checks; their executed scope is recorded below. | [MixedQueryReadsOneQualifiedSnapshotAcrossAtomicStructureAndPayloadChanges](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Snapshots.cs)<br>[ExistingHierarchyQueriesRemainComposable](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionEndpointSourcesTests.cs) |
| Q28 | Any accepts a farther matching owner; Nearest rejects the same source when the nearer candidate fails its value predicate. The false/true cases return [3] versus no rows. | [NearestCandidateFailureDoesNotFallBackToMatchingAncestor](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs) |
| Q29 | The selected nearest owner is tested against its own requested value: owner 2 satisfies !Matches and accepts sources [2,3,4]; requesting a nonmatching value rejects the source without fallback. | [NearestDoesNotInterpretApplicationPeriodsAsStructuralEdges](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs)<br>[NearestCandidateFailureDoesNotFallBackToMatchingAncestor](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs) |
| Q30 | The value predicate does not select the owner. NearestSelfHasNoMatchingAncestorFallback(3,true/false) accepts or rejects the same self candidate; a farther matching pivot cannot rescue the rejected choice. | [NearestSelfHasNoMatchingAncestorFallback](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs)<br>[ProjectedNearestIsChosenBeforeSubtreeExpansion](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs) |
| Q31 | An inclusive root or leaf selects self before evaluating its value and cannot fall back to a farther owner. A key of zero remains a valid candidate. | [NearestSelfHasNoMatchingAncestorFallback](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs)<br>[ZeroNodeKeyDoesNotConfuseMissingAndSelectedOwner](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs) |
| Q32 | A missing candidate rejects the row. SQL NULL cannot become the default key of zero or select a root. | [NoEligibleOwnerProducesNoRows](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs)<br>[ZeroNodeKeyDoesNotConfuseMissingAndSelectedOwner](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs) |
| Q33 | Any and Nearest both require the entire condition to match one owner. | [OwnerCriteriaMustMatchTheSameAncestor](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs) |
| Q34 | A hidden closer candidate cannot expose a farther matching owner. Any and complete-path Nearest retain their distinct visibility policies. | [HiddenCloserCandidateDoesNotExposeFartherMatchingOwner](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| Q35 | Any can cross a hidden node that is not a candidate. Nearest requires the complete path from the owner to the node. | [NearestRejectsHiddenIntermediateEvenWhenItIsNotACandidate](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs) |
| Q36 | A hidden ancestor above the selected owner does not affect its path check. A hidden source produces no projection. | [NearestStopsVisibilityCheckAtSelectedOwner](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs)<br>[EmptyOrInvisibleSeedCannotBeResurrected](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| Q37 | Scope-wide owner selection excludes a deeper candidate from another tree even when bounds overlap. Equal local keys and converted TreeId aliases remain isolated. | [DeeperForeignOwnerCannotAffectScopeWideSelection](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.ForeignOwners.cs)<br>[ForeignScopeEndpointCannotAdoptLocalEqualNodeKeys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Composite.cs)<br>[ConvertedTreeIdentityKeepsDatabaseDistinctAliasesIsolated](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.TreeIdentity.cs) |
| Q41 | Two outer rows for the same node use their own payloads to select candidates. Only the expected row passes. | [NearestCandidateSelectionUsesEachOuterRowsPayload](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs) |
| Q43 | Provider tests cover server translation and retained plan evidence and reject unsupported CLR predicates before commands execute. Native SQL Server execution requires supported Linux x64 qualification; local ARM skips do not establish it. | [NonTranslatablePredicateFailsBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[CompositionRetainsExecutedSqlAndNativePlanEvidence](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Plans.cs) |
| Q44 | All six APIs and mixed query shapes build without I/O while connection and command probes are armed. | [ScopelessComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs)<br>[ScopedComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs)<br>[TreeBoundComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs) |
| Q45 | List and Sum terminals each use exactly one asynchronous reader. An interceptor rejects synchronous execution. | [OneAsyncTerminalExecutesOneMixedQuery](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Async.cs) |
| Q46 | Any accepts a visible farther owner across a hidden gap. Nearest rejects the same endpoint. | [NearestRejectsHiddenIntermediateEvenWhenItIsNotACandidate](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Paths.cs) |
| U01 | Strict and inclusive combinations retain exact edge distances. Self is included only by an inclusive leg, at distance zero. | [SameDirectionWalksPreserveIntermediateMultiplicity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs)<br>[AncestorsOrSelfPreserveDistanceAcrossHiddenIntermediate](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Projections.cs) |
| U02 | The second ancestor and the nearest owner with nonmonotonic keys are selected by stored edge distance. | [AncestorDistanceSupportsNearestFirstRank](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Projections.cs)<br>[StrictNearestUsesStructuralDepthWithNonMonotonicKeys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs) |
| U03 | Strict ancestors of a root are empty. An inclusive root can expand only to its legitimate descendants, and missing or hidden seeds cannot appear downstream. | [EmptyOrInvisibleSeedCannotBeResurrected](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| U04 | Equal local keys and coordinates cannot cause rows to cross tree or scope boundaries. | [ForeignTreeEndpointCannotAdoptMatchingLocalBounds](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs)<br>[CompositePrimaryKeysKeepEqualNodeKeysAndBoundsIsolatedByScope](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Composite.cs) |
| U05 | Upward and owner operations use captured mapping roles, including named indexer mappings and fields whose names differ from those roles. | [RenamedShadowRolesPreserveIdentityAndRejectMissingOwners](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Shadow.cs)<br>[NamedIndexerMappingsPreserveExactHierarchyIdentity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Named.cs) |
| U47 | A raw projection across a hidden gap retains distance two. Complete-path validation explicitly requires endpoints in upper-to-lower order. | [HiddenAncestorDoesNotCompressStrictDistance](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs)<br>[UpwardPathRequiresUpperToLowerEndpointDirection](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| U50 | The ancestor-to-source endpoint order succeeds, and reversing those endpoints yields no rows. The edge predicate always receives the stored parent followed by the child. | [UpwardPathRequiresUpperToLowerEndpointDirection](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| U56 | The shared deferred-construction tests verify that every facade variant builds without I/O. | [ScopelessComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs)<br>[ScopedComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs)<br>[TreeBoundComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs) |
| U61 | A correctly mapped alias is accepted. Another context or an incorrect named mapping fails before commands execute. | [NestedForeignContextRootFailsBeforeEitherReader](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[NamedMappingEndpointMismatchIsRejectedBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Named.cs) |
| U62 | The model suite covers 13 shapes, all six operations, and missing, rejected, or blocked cases. Shapes include compiled, shadow, composite, backing-field, indexer, and split mappings. | [ConvertedOperatorFreeKeysPreserveNullableParentAndMissingOwnerSemantics](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Converted.cs)<br>[InheritedMappingsRetainPolymorphicIdentityAndCompletePaths](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Inheritance.cs)<br>[EntitySplitStructuralFragmentRetainsMappedIdentityAndCompletePaths](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Splitting.cs) |
| C01 | Radii of zero, one, and two and strict or inclusive descendant legs use stored depth despite an invisible intermediate level. | [DescendantRadiusDoesNotCompressInvisibleLevels](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs)<br>[SameDirectionWalksPreserveIntermediateMultiplicity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| C02 | A plain structural model supports all six APIs without type, role, signal, or metadata tables. | [DirectUnscopedFacadeComposesWithoutATreeWrapper](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Unscoped.cs)<br>[BackingFieldRolesIgnoreDecoratedClrGetters](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Fields.cs) |
| C03 | Direction changes retain the exact pivot, upper endpoint, and distance tuples. An explicit fence changes the result; traversal does not implicitly stop at the origin. | [DownThenUpPreservesTurnMultiplicityAndExplicitOriginFence](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Directions.cs) |
| C04 | A six-row initial bag and an independent parent-list oracle verify every source, pivot, target, upper endpoint, and leg distance in three-leg walks. | [UpThenDownPreservesPerSourceBagAndDistances](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Directions.cs)<br>[UpDownUpRetainsTheCompleteWalkBag](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| C05 | A six-row initial bag and an independent parent-list oracle verify every source, pivot, target, upper endpoint, and leg distance in three-leg walks. | [UpThenDownPreservesPerSourceBagAndDistances](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Directions.cs)<br>[UpDownUpRetainsTheCompleteWalkBag](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| C06 | A six-row initial bag and an independent parent-list oracle verify every source, pivot, target, upper endpoint, and leg distance in three-leg walks. | [UpThenDownPreservesPerSourceBagAndDistances](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Directions.cs)<br>[UpDownUpRetainsTheCompleteWalkBag](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| C07 | Path, Any, and Nearest retain two existing valid occurrences as two. A null endpoint removes only its own occurrence. | [OwnerFiltersPreserveDuplicateOuterRowsWithoutMultiplyingThem](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Directions.cs)<br>[MissingEndpointOnlyRemovesItsOwnOccurrences](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs) |
| C08 | A join yields [3, 3, 4, 4], while an existence filter yields [3, 4]. | [JoinAndExistenceHaveDifferentMultiplicity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Multiplicity.cs) |
| C09 | Explicit distinctness by target, source and target, or complete walk produces the intended tuple bag; ordinary bag semantics retain duplicates. | [ExplicitDistinctUsesOnlyTheChosenResultGrain](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Multiplicity.cs) |
| C10 | Any selects unique anchors from two existing payload rows. Joining those anchors back before owner filtering retains payload values 11 and 22, with each row's criteria independently accepted or rejected. | [UniqueAnchorSelectionRestoresEachPayloadBeforeOwnerFiltering](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Origins.cs) |
| C11 | All eight self and strict combinations for UpUp and DownDown match the complete tuple bag from an independent parent-list oracle. | [SameDirectionWalksPreserveIntermediateMultiplicity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| C12 | Both directions retain the stored depth difference across a hidden intermediate node. Distance never becomes a count of visible rows. | [HiddenAncestorDoesNotCompressStrictDistance](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs)<br>[DescendantRadiusDoesNotCompressInvisibleLevels](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| C13 | Source 3 selects pivot 2 at distance one, and source 6 selects pivot 1 at distance two. Rank two omits an origin without enough pivots; ranking is independent per origin. | [NearestPivotIsChosenIndependentlyForEachOrigin](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Origins.cs)<br>[NthEligiblePivotRankIsCorrelatedPerOrigin](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Origins.cs) |
| C14 | Blocking any one of three legs rejects the entire occurrence. Opening every leg accepts it. | [EveryMixedLegMustPassItsOwnDirectEdgeRule](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| C15 | An open target or a self-expansion cannot restore a row rejected by the first leg. | [LaterSelfExpansionCannotResurrectEarlierBlockedLeg](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs)<br>[ClosedUpwardLegCannotReappearThroughOpenDownwardLeg](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Directions.cs) |
| C16 | An UpDown walk across a hidden intermediate node retains the raw and Any tuple (3, 1, 6, 2, 2). Complete-path and Nearest variants reject the same walk. | [DirectionChangePreservesExplicitHiddenPathPolicy](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| C17 | A failing self-candidate prevents expansion from a farther pivot. Strict selection excludes self from the candidate set before choosing an owner. | [ProjectedNearestIsChosenBeforeSubtreeExpansion](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs)<br>[StrictNearestUsesStructuralDepthWithNonMonotonicKeys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs) |
| C18 | A failing self-candidate prevents expansion from a farther pivot. Strict selection excludes self from the candidate set before choosing an owner. | [ProjectedNearestIsChosenBeforeSubtreeExpansion](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs)<br>[StrictNearestUsesStructuralDepthWithNonMonotonicKeys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Owners.cs) |
| C19 | Twelve cases combine pair shapes, source projections, and Any or Nearest to verify the selected alias. Source and Pivot selections have distinct effects. | [PairShapesUseTheSelectedOwnerEndpoint](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Multiplicity.cs)<br>[OwnerPredicateUsesTheExplicitlySelectedAlias](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs) |
| C20 | MemberInit and anonymous pair projections joined to payload retain their real hierarchy aliases and separate payload values. Separate rejection cases cover an incorrect mapping. | [MemberInitAndIndependentPayloadRetainTheSelectedMapping](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[PairShapesUseTheSelectedOwnerEndpoint](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Multiplicity.cs) |
| C21 | All three endpoint filters reject a foreign-scope row instead of rebinding it to a local row with the same NodeKey. | [ForeignScopeEndpointCannotAdoptLocalEqualNodeKeys](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Composite.cs) |
| C22 | An endpoint from another runtime tree is excluded. Matching mappings and coordinates cannot substitute for local identity. | [ForeignTreeEndpointCannotAdoptMatchingLocalBounds](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs)<br>[IgnoringVisibilityKeepsScopeAndTreeBindings](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs) |
| C23 | A correct named alias is accepted. Selecting an alias from the wrong named mapping is rejected before any command executes. | [SameClrTypeDoesNotMakeNamedMappingsInterchangeable](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionEndpointTests.cs)<br>[MappedAliasesAllowIndependentPayloadRoots](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionEndpointTests.cs) |
| C24 | An independent payload query root is allowed when the selected hierarchy alias has the correct mapping. | [MappedAliasesAllowIndependentPayloadRoots](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionEndpointTests.cs)<br>[MemberInitAndIndependentPayloadRetainTheSelectedMapping](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs) |
| C25 | An EnumerableQuery is rejected before enumeration. Validation does not reject a query merely because a method is named AsEnumerable. | [EnumerableQueryIsRejectedWithoutEnumeration](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionSourceTests.cs)<br>[ArrayQueryCannotSupplyHierarchyAliases](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionSourceTests.cs) |
| C26 | A constructed entity endpoint and a nested foreign context are rejected before either context executes a command. | [ConstructedEndpointIsRejectedBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[NestedForeignContextRootFailsBeforeEitherReader](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs) |
| C27 | Explicitly ignoring visibility filters restores visibility only. Scope and tree bindings remain enforced. | [IgnoringVisibilityKeepsScopeAndTreeBindings](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs) |
| C28 | Strict traversal from a root and traversal from an absent or hidden source cannot gain downstream rows through a later join. | [EmptyOrInvisibleSeedCannotBeResurrected](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs) |
| C29 | All six methods and mixed query shapes build without I/O under connection and command probes that would reject execution. | [ScopelessComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs)<br>[ScopedComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs)<br>[TreeBoundComposersBuildWithoutIo](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionDeferredTests.cs) |
| C30 | One asynchronous reader uses the caller's token before and during reading. Cancellation cannot return partial success. | [OneAsyncTerminalExecutesOneMixedQuery](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Async.cs)<br>[CancellationBeforeExecutionDoesNotProduceAResult](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Async.cs)<br>[CancellationDuringReaderDoesNotReturnPartialSuccess](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Async.cs) |
| C31 | Medium, shared million-row and ten-million-row, deep, and consumer profiles assert command counts, scalar output, no tracker or entity growth, and warm allocation behavior. Executed profile coverage is recorded below. | [MediumCompositionKeepsCommandsMaterializationAndConstructionBounded](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/HierarchyCompositionCapacityTests.cs)<br>[SelectiveDeepCompositionHasBoundedResultsAndInspectablePlans](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Scaling.cs) |
| C32 | The model matrix verifies database identity rather than CLR object or reference equality. | [GeneratedModelUsesCapturedPrincipalCollationForParentAliases](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Compiled.cs)<br>[BinaryKeysRetainDatabaseValueEqualityInEveryComposer](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Binary.cs)<br>[CompositePrimaryKeysKeepEqualNodeKeysAndBoundsIsolatedByScope](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Composite.cs) |
| C33 | The first-row reader boundary overlaps an atomic Move or Rebuild transaction that changes periods and facts. Returned tuples must match the exact before or after snapshot. | [MixedQueryReadsOneQualifiedSnapshotAcrossAtomicStructureAndPayloadChanges](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Consistency/HierarchyCompositionConsistencyTests.Snapshots.cs) |
| C34 | Unscoped, scoped, and tree-bound facades compose correctly. All six APIs reject a scoped hierarchy without a scope binding. | [DirectUnscopedFacadeComposesWithoutATreeWrapper](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Unscoped.cs)<br>[ScopedHierarchyRequiresBindingForEveryComposer](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionArgumentTests.cs)<br>[RenamedShadowRolesPreserveIdentityAndRejectMissingOwners](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Shadow.cs) |
| C35 | Changing scalar values retains the SQL text and parameter names while producing the separately expected results. | [ChangingScalarCriteriaKeepsCommandShapeAndBindings](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs) |
| C36 | Nested lambda parameters with repeated names are substituted by identity and produce the expected evaluated result. The original lambdas remain unchanged. | [NestedLambdasAreSubstitutedByIdentityRatherThanName](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionExpressionTests.cs) |
| C37 | Source tests cover server translation of correlated predicates and three-leg tuple bags. Fast SQL-boundary regressions cover the independent path proof, strict nearest selection and decorrelatable mixed-direction pivots; native plans qualify their actual work. | [NonTranslatablePredicateFailsBeforeSql](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Translation.cs)<br>[UpDownUpRetainsTheCompleteWalkBag](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs)<br>[MultipleAnchorsCorrelateThreeSourcesAndDateOnlyEdges](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Projections.cs) |
| C38 | A LeftJoin with a null endpoint and valid duplicate rows retains exactly [4, 4]. | [MissingEndpointOnlyRemovesItsOwnOccurrences](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Identity.cs) |
| C39 | Without a fence, traversal can return ancestors above the origin. Adding UpDistance <= DownDistance changes the exact expected tuples. | [DownThenUpPreservesTurnMultiplicityAndExplicitOriginFence](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Directions.cs) |
| C40 | Upward traversal passes each stored parent and child in that order. Each mixed leg independently enforces its own blocking rule. | [UpwardPathRequiresUpperToLowerEndpointDirection](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Visibility.cs)<br>[EveryMixedLegMustPassItsOwnDirectEdgeRule](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |
| C41 | The final tracking, no-tracking, or identity-resolution policy controls entity occurrences. Queries do not refresh stale tracked entities, and scalar DTO projections add no tracker entries. | [FinalTrackingPolicyControlsRepeatedEntityOccurrences](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Tracking.cs)<br>[TrackingDoesNotRefreshAnExistingEntityFromTheQuerySnapshot](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Tracking.cs)<br>[ScalarProjectionDoesNotAddTrackedEntities](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Tracking.cs) |
| C42 | An inclusive two-leg chain produces six tuples and a repeated end target. Each strict combination has a separate expected tuple bag. | [SameDirectionWalksPreserveIntermediateMultiplicity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.Walks.cs) |

## Structural-pair projection regressions

- [LeafSelectionPreservesSelfVisibilityAndEmptyDescendants](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Projections.cs)
  accepts exactly the selected visible leaf and rejects the filtered leaf.
- [OverlappingRootAndLeafPreserveClientProjectionAndOccurrences](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/HierarchyCompositionTests.Projections.cs)
  preserves seven occurrences while applying a legal terminal scalar client
  projection after SQL coordinate selection.
- [SubtreeEntityProjectionPreservesOwnedNavigations](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Navigation.cs)
  retains owned reference and collection auto-includes with overlapping anchors.
- [SubtreeProjectionPreservesOwnedAndOrdinaryCollectionDtos](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.Navigation.cs)
  retains owned and ordinary collection DTOs on both anchor and related
  endpoints without materializing entities.
  Both navigation tests compare the compositor with an ordinary EF range join
  and cover visible, filtered, and missing endpoints.

These are source contracts for Q02, Q06, C07, C20 and C41. Internal unique
structural keys do not deduplicate equal projected values or payload bags.

The upward counterparts independently cover strict and inclusive traversal:

- [AncestorEntityPairsPreserveOwnedAutoIncludes](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.AncestorNavigation.cs)
  preserves both endpoints' owned references and collection auto-includes,
  including repeated ancestors reached from different visible sources.
- [AncestorProjectionPreservesOwnedAndOrdinaryCollectionDtos](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.AncestorNavigation.cs)
  preserves owned and ordinary collection DTO bags from both endpoints without
  materializing entities. Both upward tests compare exact source/ancestor/distance
  tuples with independent fixture expectations, reject hidden or missing selected
  sources, and retain one SQL reader with an empty tracker.

The [typed provider strategy tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaQueryFeaturesTests.cs)
verify the actual configured MySQL/MariaDB family, exact native assembly, typed
method identities, and zero connection/command starts. Other adapters expose
no foreign Doka primitive and keep their portable range strategy. The
[adapter identity tests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/OptionsExtensionTests.Identity.cs)
independently reject equal-named foreign load-context owners and exercise
collectible registration ownership. Synthetic optional API discovery tests
are replaced by direct package/compiler binding and these actual registration
contracts. They do not replace native plans or the existing 160 semantic IDs.

## Ordinary strict source bounds and origin witnesses

[Sparse ancestor identity controls](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.SparseAncestorIdentity.cs)
retain equal-key foreign Scopes, equal bounds in separate trees, hidden/missing
endpoints, mapped collections, and bounds semantics despite a damaged Parent
link. [Mixed-direction capacity profiles](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/CompositionCapacityProfiles.Mixed.cs)
combine upward pairs, a relational pivot, downward pairs, complete paths and
aggregation over the same persisted population. The original
`UpDownUpRetainsTheCompleteWalkBag` cases exercise repeated direction changes
with zero through three complete-path filters; targeted success on both native
engines does not by itself qualify the full provider suite. Native work is qualified
separately in [Performance](performance.md#ordinary-strict-source-bounds-and-membership-witnesses).

## Longer-path strategy regressions

The [fast Doka translation checks](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionPathTranslationTests.cs)
require the invariant path/Nearest grouped MAX shape and retain COUNT for
row-dependent edge rules. They reject the previous expression before database
I/O. They do not replace actual native plan or performance evidence.

Paths beyond the zero/one/two-edge fast cases have independent parent-walk
expectations. They cover both row-independent edge rules and per-row dates,
as well as the structural path required by Nearest:

- [LongPathsRequireEveryActualParent](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.LongPaths.cs)
  rejects missing, skipped and foreign-tree parents. Its equal-key foreign-scope
  decoy preserves the valid local path without bypassing the database's
  composite foreign key.
- [LongPathsRespectVisibilityOnlyBelowTheirUpperEndpoint](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.LongPaths.cs)
  rejects one or two hidden intermediate nodes and permits a visible suffix
  whose hidden nodes lie above the selected owner.
- [LongPathsIgnoreIncomingAndOffPathFailures](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.LongPaths.cs)
  keeps the upper endpoint's incoming edge and an invalid sibling out of the
  selected path.
- [LongPathsKeepOriginBagsAndCompleteBoundIdentities](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.LongPaths.cs)
  retains repeated origins and per-date results while rejecting foreign
  endpoints with otherwise matching coordinates.
- [DenseDisjointFailuresPreserveOnlyTheirOwnParentWalks](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.DensePaths.cs)
  checks multiple lower endpoints, repeated original occurrences and many
  disjoint failing branches against actual Parent walks, including hidden
  intermediates and foreign scope/tree decoys.
- [DensePathBoundariesFollowActualParentIdentity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.DensePathBoundaries.cs)
  checks compact, adjacent and interior coordinate windows, both endpoint
  boundaries, absent failure blocks and off-fringe siblings. Independent,
  row-aware and structural-nearest queries retain their distinct contracts.
- [CompositionPathPartitionTests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionPathPartitionTests.cs)
  exercises the real expression arithmetic near large square boundaries and
  Int64.MaxValue. These pure arithmetic probes do not claim that shifted or
  gapped coordinates satisfy the persisted dense-coordinate contract.
- [IndependentNearestSelectionPreservesStructuralOwnerAndOriginalBag](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.NearestBlocks.cs)
  chooses among several ancestor sections with nonmonotonic keys, including
  real key zero. It rejects a selected owner without farther fallback,
  distinguishes visibility above and below that owner, rejects off-path-only
  candidates and retains every original occurrence across foreign decoys.
- [IndependentNearestChoiceKeepsRowValuesAndUpstreamPaging](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.NearestBlocks.cs)
  preserves distinct value decisions for duplicate endpoints and keeps caller
  filters, ordering, Skip and Take before the nearest-owner filter.
- [IndependentNearestPreservesVolatilePagedSourceCardinality](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionSemanticTests.NearestBlocks.cs)
  retains all ten qualifying rows from a random ordered/paged source without
  independently resampling the input for candidate selection. The assertion
  fixes cardinality rather than depending on which random keys are selected.
- [LongConvertedKeyPathsRetainNullableParentIdentity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.LongPaths.cs)
  and [LongEntitySplitPathsRetainStructuralFragmentIdentity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.LongPaths.cs)
  retain converted nullable Parent identity and secondary-fragment bindings.

These tests supplement Q04, Q07-Q09, Q21 and the matching composition contracts.
They do not expand query composition into an arbitrary-corruption validator:
valid nested-set bounds and depths remain a precondition.

The [MySQL/MariaDB native work family](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Querying/Composition/Performance/DensePathWorkTestBase.cs)
uses actual public path and nearest filters over physical payload joins. It
retains the original chain placements, adds million-node controls, rejecting
fringes, dense candidate acceptance/rejection, disjoint branches and complete
scope forests with overlapping decoy coordinates. Independent Parent walks
define the expected bags. Construction bytes, scalar command count, parameters,
entity/tracker counts and actual native work are separate assertions; elapsed
time is not a gate. The original native guards and their budgets remain in place.
Dense nearest controls also require a preceding block when the lower endpoint's
own block has no eligible candidate; forest variants retain both authorized
tree identities and reject an otherwise matching foreign-scope population.

The [native counter controls](../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Querying/Composition/Performance/DensePathNativeWorkTests.cs)
reject estimated-only, malformed and incomplete evidence. They distinguish
physical input, pre/post-index filtering, known materialized output and
unexposed output. An unknown relation cannot hide a measured excessive output.

## Expression and consumer work regressions

These additional controls preserve the existing contract IDs while checking
the implementation boundaries used to reduce expression and database work:

| Additional boundary | Positive and rejection evidence |
| --- | --- |
| Exact mapped scalar reads | [CompositionMappedMemberTests](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionMappedMemberTests.cs): actual deferred SQLite queries for renamed properties, differently named backing fields, field-only, shadow, indexer and complex roles; both matching and rejecting predicates; throwing CLR getters never execute. |
| Short-path direct links | [MappedShortPathsRejectEveryBrokenDirectEdge](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionTests.MappedShortPaths.cs): valid one/two-edge paths, each wrong Parent, nonconsecutive depths, blocked predicates, hidden endpoints/middle and incorrect middle bounds. Existing forest, principal-collation and nullable converted-key controls remain required. |
| Sparse ancestor identity | [Sparse-identity controls](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/HierarchyCompositionModelTests.SparseAncestorIdentity.cs): overlapping foreign Scope/Tree keys and bounds, missing/hidden aliases, damaged adjacency under pure bounds semantics, and owned collection DTO identity. These semantic checks do not claim selective native work. |
| Native deep signal work | [Deep signal work controls](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/HierarchyCompositionCapacityTests.DeepSignalProofReuse.cs): control and repeated-fact profiles over 100,001 persisted nodes; unchanged results, commands, entities and construction budgets, plus a fact-independent MySQL operator-work bound. |

The real chain capacity adapter also retains every exact reader's plan for all
2/4/8-hop original and expanded profiles. Diagnostic EXPLAIN is outside timing;
its repeated whole-chain header is not a per-reader duration or physical row
count. The failed sparse range experiments remain rejected evidence and are
not represented as green performance coverage by the identity tests above.


The complete signal read qualification (planned documentation: `qualification/query-performance.md#historical-report-construction-and-materialization`)
records the historical execution boundary for its catalog, validation and query
changes. It keeps old-binary negative controls separate from that source's
test outcomes and distinguishes unit allocation fixtures from process-wide
complete-read allocations. This source-coverage map alone does not establish
successful execution of the full native-provider or deep-profile matrix.

- [Endpoint construction](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionEndpointConstructionTests.cs)
  proves source visibility once per operation, rejects foreign or constructed
  aliases, retains changed filter policies, and never reads an unrelated
  payload query's expression merely to resolve the selected leg.
- [Visibility traversal](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionVisibilityTraversalTests.cs)
  retains shared and distinct source semantics, independent operations, captured
  query rejection, zero getter execution and bounded allocation. The
  [exact-bound controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionVisibilityTraversalTests.BoundCandidate.cs)
  expose a safe first visit followed by unsafe custom dispatch, MethodInfo or
  PropertyInfo metadata inside the candidate. Opaque candidates must be visited
  again; only an immutable exact binding qualifies for local proof reuse.
  These tests do not promise linear work for arbitrary shared payload graphs.
- [Direct ancestor coordinates](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionAncestorConstructionTests.cs)
  reject an internally captured IQueryable, retain both endpoint proofs after
  the coordinate boundary, and construct/translate both Doka dialects without I/O.
- [Short-path identity](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Semantics/HierarchyCompositionTests.ShortPathIdentity.cs)
  permits the local two-edge bridge and rejects an A-to-B-to-A bridge in both
  forest and exact-tree queries, despite matching coordinates and depths.
- [Standalone large consumer profiles](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/HierarchyCompositionCapacityTests.Consumers.cs)
  run the real sample builders on wide, balanced and depth-100,000 structures
  independently of the generic selective-pair qualification. Existing combined
  population checks remain intact.
- [SQLite instruction-guard controls](../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Querying/Composition/SqliteInstructionBudgetTests.cs)
  separately verify a finite aggregate's exact successful result, deterministic
  interruption of a quadratic read, and successful reuse of the same connection
  after disposing an interrupted handler. These controls protect the guard
  itself; they do not replace the actual deep ordinal outcome checks.

These are source references, not claims of a complete native or hosted pass.
Current executions and allocation measurements require their dated source and
binary identity, as described in the performance guide.

## Sample migration regressions

The executable console samples publish Doka and SQLite migration lines. Their
shared query models also execute on PostgreSQL and SQL Server; that does not
create additional console migration sets.


## Execution qualification

The 2026-10-26/27 synthetic comparison (planned documentation: `qualification/query-performance.md#synthetic-signal-lab-2026-10-26`)
records completed native read, construction, materializer, MariaDB scale and
prespecified construction-repeat observations. The four original-before
100,001-node controls and their current-source counterparts pass with unchanged
work/allocation budgets, separately from observational timing.
The record does not establish full capacity, native SQL Server or hosted RC
qualification.

- [Query/native work](qualification/query-composition.md): exact structural
  bags, dense/deep plans, optional-index costs and adverse/unknown outcomes.
- Performance/allocation (planned documentation: `qualification/query-performance.md`): construction,
  complete `ReadAsync`, isolated materialization and framework experiments.
- [Provider/migration integration](qualification/provider-migrations.md): exact
  local packages, actual schema/data/history/replay and retained rejection.

Qualify each new source with its actual DLL hashes, selected tests, runtime,
engine/image, application indexes and physical populations. Native SQL Server
requires supported Linux x64 execution with no platform skips. Translation
sentinels, ARM skips and another engine's success cannot replace that evidence.
Local observations do not establish a hosted CI/RC or publication pass.
Scenario numbers in sample fixtures are not physical hierarchy depths.
