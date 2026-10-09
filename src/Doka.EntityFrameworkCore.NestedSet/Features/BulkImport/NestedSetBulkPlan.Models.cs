namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Retains one import-owned entry and its immutable adjacency and rollback state.</summary>
    internal sealed class Node
    {
        /// <summary>Snapshots every mapped value before structural assignment or generated-value propagation.</summary>
        internal Node(
            TEntity entity,
            NestedSetMapping<TEntity, TKey, TScope> map,
            int parent,
            int relativeDepth,
            bool hasAssignedKey
        )
        {
            Entity = entity;
            Parent = parent;
            RelativeDepth = relativeDepth;
            HasAssignedKey = hasAssignedKey;

            CurrentKey = NestedSetTypedValue<TKey>.Snapshot(
                map.KeyProperty,
                NestedSetTypedValue<TKey>.Read(entity, map.KeyProperty));

            Original = OriginalStructure.Capture(entity, map);
        }

        /// <summary>Gets the caller-owned detached entity.</summary>
        internal TEntity Entity { get; }

        /// <summary>Gets the parent input index, or minus one for a destination sibling root.</summary>
        internal int Parent { get; }

        /// <summary>Gets the depth within the new branch independently of its destination.</summary>
        internal int RelativeDepth { get; }

        /// <summary>Gets or sets the first direct child index, or minus one for a leaf.</summary>
        internal int FirstChild { get; set; } = -1;

        /// <summary>Gets or sets the last direct child index used for constant-time input-order appends.</summary>
        internal int LastChild { get; set; } = -1;

        /// <summary>Gets or sets the next sibling index, or minus one at the end of a sibling group.</summary>
        internal int NextSibling { get; set; } = -1;

        /// <summary>Gets or sets the number of direct children.</summary>
        internal int ChildCount { get; set; }

        /// <summary>Gets whether the identity was assigned before tracking or provider value generation.</summary>
        internal bool HasAssignedKey { get; }

        /// <summary>Gets or sets the parent value that the insertion wave must preserve.</summary>
        internal NestedSetParent<TKey> StagedParent { get; set; }

        /// <summary>Gets or sets whether the parent must be assigned after every imported row exists.</summary>
        internal bool NeedsParentUpdate { get; set; }

        /// <summary>Gets or sets the coordinates validated immediately after the insertion save.</summary>
        internal Geometry StagedGeometry { get; set; }

        /// <summary>Gets or sets the final coordinates computed independently of primary-key generation.</summary>
        internal Geometry Geometry { get; set; }

        /// <summary>Gets or sets the assigned or store-generated identity used by later bounded batches.</summary>
        internal TKey CurrentKey { get; set; }

        /// <summary>Gets the assigned or captured store-generated identity in its mapped API type.</summary>
        internal TKey Key => CurrentKey;

        /// <summary>Gets non-sentinel caller values, or null when metadata sentinels fully describe rollback.</summary>
        internal OriginalStructure? Original { get; }

        // WHY: Provider-generated properties have heterogeneous CLR types and can contain null or metadata sentinels.
        // Rollback retains their exact sparse metadata representation rather than normalizing them to identity roles.
        /// <summary>Gets exact original generated values, or null when every value used its mapped sentinel.</summary>
        internal object?[]? OriginalGeneratedValues { get; private set; }

        /// <summary>Gets whether generated values were captured before this input entered an insertion batch.</summary>
        internal bool GeneratedValuesCaptured { get; private set; }

        /// <summary>Captures sparse rollback state before EF can propagate provider-generated CLR values.</summary>
        internal void CaptureGeneratedValues(
            IReadOnlyList<IProperty> properties
        )
        {
            object?[]? captured = null;

            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties[index];

                if (!NestedSetRefreshProperties.AppliesTo(property, Entity))
                {
                    // WHY: A base-facade plan combines generated leaves from its participating subtypes.
                    // Each input owns only the leaves present in its configured CLR inheritance chain.
                    continue;
                }

                // WHY: Creating a detached EF entry before an application getter succeeds retains the input
                // on failure. The public containing-entity getter also resolves nested complex CLR leaves.
                var value = property.GetGetter().GetClrValueUsingContainingEntity(Entity);

                if (captured is null
                    && NestedSetStructuralValue.Matches(property, value, property.Sentinel))
                {
                    continue;
                }

                if (captured is null)
                {
                    captured = new object?[properties.Count];

                    for (var previous = 0; previous < index; previous++)
                    {
                        captured[previous] = NestedSetStructuralValue.Snapshot(
                            properties[previous],
                            properties[previous].Sentinel);
                    }
                }

                captured[index] = NestedSetStructuralValue.Snapshot(property, value);
            }

            OriginalGeneratedValues = captured;
            GeneratedValuesCaptured = true;
        }
    }

    /// <summary>Retains rollback values only for inputs whose structure differs from mapped sentinels.</summary>
    internal sealed class OriginalStructure
    {
        private readonly byte _fields;
        private readonly long _left;
        private readonly long _right;
        private readonly int _depth;
        private readonly long _position;

        // WHY: Detached inputs may contain null or sentinel values that are not valid runtime identities.
        // Sparse metadata-shaped snapshots preserve exact rollback values; active Scope, TreeId and Parent stay typed.
        // Validated coordinates are always long/int, so retaining their native values avoids four boxes per input.
        /// <summary>Creates one sparse snapshot after determining that at least one value is non-sentinel.</summary>
        private OriginalStructure(
            byte fields,
            object? scope,
            object? treeId,
            object? parent,
            long left,
            long right,
            int depth,
            long position
        )
        {
            _fields = fields;
            Scope = scope;
            TreeId = treeId;
            Parent = parent;
            _left = left;
            _right = right;
            _depth = depth;
            _position = position;
        }

        /// <summary>Gets the original non-sentinel Scope, or null when Scope used its sentinel.</summary>
        internal object? Scope { get; }

        /// <summary>Gets the original non-sentinel TreeId, or null when TreeId used its sentinel.</summary>
        internal object? TreeId { get; }

        /// <summary>Gets the original non-sentinel Parent, or null when Parent used its sentinel.</summary>
        internal object? Parent { get; }

        /// <summary>Returns the captured Scope representation, including null, or the mapped sentinel.</summary>
        internal object? ScopeOr(
            object? sentinel
        ) => ValueOr(1 << 0, Scope, sentinel);

        /// <summary>Returns the captured TreeId representation, including null, or the mapped sentinel.</summary>
        internal object? TreeIdOr(
            object? sentinel
        ) => ValueOr(1 << 1, TreeId, sentinel);

        /// <summary>Returns the captured Parent representation, including null, or the mapped sentinel.</summary>
        internal object? ParentOr(
            object? sentinel
        ) => ValueOr(1 << 2, Parent, sentinel);

        /// <summary>Returns the captured left representation, including null, or the mapped sentinel.</summary>
        internal object? LeftOr(
            object? sentinel
        ) => (_fields & (1 << 3)) != 0 ? _left : sentinel;

        /// <summary>Returns the captured right representation, including null, or the mapped sentinel.</summary>
        internal object? RightOr(
            object? sentinel
        ) => (_fields & (1 << 4)) != 0 ? _right : sentinel;

        /// <summary>Returns the captured depth representation, including null, or the mapped sentinel.</summary>
        internal object? DepthOr(
            object? sentinel
        ) => (_fields & (1 << 5)) != 0 ? _depth : sentinel;

        /// <summary>Returns the captured position representation, including null, or the mapped sentinel.</summary>
        internal object? PositionOr(
            object? sentinel
        ) => (_fields & (1 << 6)) != 0 ? _position : sentinel;

        /// <summary>Captures exact non-sentinel structure without allocating for a conventional new entity.</summary>
        internal static OriginalStructure? Capture(
            TEntity entity,
            NestedSetMapping<TEntity, TKey, TScope> map
        )
        {
            byte fields = 0;
            var scope = CaptureValue(entity, map.ScopeProperty, 1 << 0, ref fields);
            var treeId = CaptureValue(entity, map.TreeIdProperty, 1 << 1, ref fields);
            var parent = CaptureValue(entity, map.ParentProperty, 1 << 2, ref fields);
            var left = CaptureValue(entity, map.LeftProperty, 1 << 3, ref fields);
            var right = CaptureValue(entity, map.RightProperty, 1 << 4, ref fields);
            var depth = CaptureValue(entity, map.DepthProperty, 1 << 5, ref fields);
            var position = CaptureValue(entity, map.PositionProperty, 1 << 6, ref fields);

            return fields == 0
                ? null
                : new OriginalStructure(
                    fields,
                    scope,
                    treeId,
                    parent,
                    left is null ? 0L : (long)left,
                    right is null ? 0L : (long)right,
                    depth is null ? 0 : (int)depth,
                    position is null ? 0L : (long)position);
        }

        /// <summary>Returns an exact snapshot only when the detached value differs from its mapped sentinel.</summary>
        private static object? CaptureValue(
            TEntity entity,
            IProperty? property,
            byte field,
            ref byte fields
        )
        {
            if (property is null)
            {
                return null;
            }

            var value = NestedSetStructuralValue.Read(entity, property);

            if (NestedSetStructuralValue.Matches(property, value, property.Sentinel))
            {
                return null;
            }

            fields |= field;

            return NestedSetStructuralValue.Snapshot(property, value);
        }

        /// <summary>Distinguishes a captured null from an omitted value through the compact field bitmap.</summary>
        private object? ValueOr(
            int field,
            object? value,
            object? sentinel
        ) => (_fields & field) != 0 ? value : sentinel;
    }

    /// <summary>Stores complete geometry independently of generated keys and repair allocations.</summary>
    /// <param name="Left">The inclusive left interval boundary.</param>
    /// <param name="Right">The inclusive right interval boundary.</param>
    /// <param name="Depth">The zero-based depth in the destination forest.</param>
    /// <param name="Position">The zero-based sibling position.</param>
    internal readonly record struct Geometry(
        long Left,
        long Right,
        int Depth,
        long Position
    );

    /// <summary>Retains one active ancestor and its next child with traversal memory proportional to depth.</summary>
    /// <param name="Index">The active node index.</param>
    /// <param name="NextChild">The next direct child index, or minus one when the node can close.</param>
    /// <param name="NextPosition">The dense position assigned to the next direct child.</param>
    private readonly record struct TraversalFrame(
        int Index,
        int NextChild,
        long NextPosition
    );
}
