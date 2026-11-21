namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetTrackerSnapshot
{
    /// <summary>Shares one retained storage location between the scalar leaves of a complex struct.</summary>
    private sealed class ComplexClrLocation
    {
        private readonly object? _container;
        private readonly bool _hasCurrentPath;
        private readonly int[] _ordinals;
        private readonly IComplexProperty _property;
        private readonly object _root;

        /// <summary>Retains mapped current or original-only storage without creating an alternate CLR graph.</summary>
        internal ComplexClrLocation(
            object captured,
            object root,
            int[] ordinals,
            bool hasCurrentPath,
            object? container,
            IComplexProperty property
        )
        {
            Captured = captured;
            _root = root;
            _ordinals = ordinals;
            _hasCurrentPath = hasCurrentPath;
            _container = container;
            _property = property;
        }

        /// <summary>Gets the boxed preimage used while independently comparer-snapshotting scalar leaves.</summary>
        internal object Captured { get; }

        /// <summary>Whether this struct or a containing struct is mapped as a nullable value type.</summary>
        internal bool HasNullableContainer =>
            Nullable.GetUnderlyingType(_property.ClrType) is not null
            || _container is ComplexClrLocation { HasNullableContainer: true };

        /// <summary>Reads actual restored storage rather than the stale box retained at capture time.</summary>
        internal object? Read(
            IProperty property
        ) => _hasCurrentPath
            ? property
                .GetGetter()
                .GetClrValueUsingContainingEntity(_root, _ordinals)
            : property
                .GetGetter()
                .GetClrValue(ReadContainer());

        /// <summary>Restores one scalar through mapped storage, including containing-struct copyback.</summary>
        internal void Restore(
            IProperty property,
            object? value
        )
        {
            // WHY: EF's containing-entity setter cannot be built through Nullable<T>; its compiled member
            // access targets the wrapper type. Those paths copy back through their retained containers instead.
            if (_hasCurrentPath && !HasNullableContainer)
            {
                NestedSetComplexEntryAccess
                    .Setter(property)
                    .SetClrValueUsingContainingEntity(_root, _ordinals, value);

                return;
            }

            // WHY: Deleted original-only elements can have no current ordinal. Their captured original
            // storage is independent of the current graph; copy a struct back through that storage only.
            var instance = ReadContainer();
            SetMappedClrValue(instance, property, value);
            CopyBack(instance);
        }

        /// <summary>Reads the struct reference through its retained containing mapped member.</summary>
        private object ReadContainer() => _property
            .GetGetter()
            .GetClrValue(
                _container is ComplexClrLocation parent
                    ? parent.ReadContainer()
                    : _container!)!;

        /// <summary>Copies a changed original-only box through each containing value-type boundary.</summary>
        private void CopyBack(
            object value
        )
        {
            var container = _container is ComplexClrLocation parent
                ? parent.ReadContainer()
                : _container!;

            SetMappedClrValue(container, _property, value);

            if (_container is ComplexClrLocation location)
            {
                location.CopyBack(container);
            }
        }
    }
}
