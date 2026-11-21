namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetComplexEntryAccess
{
    /// <summary>Validates and compiles this recovery-only framework seam once for the loaded EF assembly.</summary>
    private sealed partial class Bindings
    {
        /// <summary>Closes exact native signatures before the first complex snapshot can be accepted.</summary>
        internal Bindings()
        {
            var assembly = typeof(IUpdateEntry).Assembly;
            var nativeBase = RequireType(assembly, "InternalEntryBase");
            var nativeComplex = RequireType(assembly, "InternalComplexEntry");
            var nativeInterface = RequireType(assembly, "IInternalEntry");
            var runtimeProperty =
                assembly.GetType("Microsoft.EntityFrameworkCore.Metadata.Internal.IRuntimePropertyBase")
                ?? throw new InvalidOperationException("EF Core does not expose the expected mapped setter contract.");

            if (!nativeBase.IsAssignableFrom(nativeComplex)
                || !nativeInterface.IsAssignableFrom(nativeBase))
            {
                throw new InvalidOperationException("EF Core's native complex entry inheritance is incompatible.");
            }

            var owner = Expression.Parameter(typeof(object), "owner");
            var complex = Expression.Parameter(typeof(object), "complex");
            var member = Expression.Parameter(typeof(MemberEntry), "member");
            var property = Expression.Parameter(typeof(IComplexProperty), "property");
            var scalar = Expression.Parameter(typeof(IProperty), "scalar");
            var metadata = Expression.Parameter(typeof(IPropertyBase), "metadata");
            var original = Expression.Parameter(typeof(bool), "original");
            var ordinal = Expression.Parameter(typeof(int), "ordinal");
            var state = Expression.Parameter(typeof(EntityState), "state");
            var currentCount = Expression.Parameter(typeof(int), "currentCount");
            var originalCount = Expression.Parameter(typeof(int), "originalCount");
            var modified = Expression.Parameter(typeof(bool), "modified");
            var nativeOwner = Expression.Convert(owner, nativeBase);
            var nativeElement = Expression.Convert(complex, nativeComplex);

            Setter = BindSetter(runtimeProperty, metadata);

            CreateCollection = BindCreateCollection(runtimeProperty, property, currentCount);

            Owner = BindOwner(nativeBase, nativeInterface, member);

            Collection = BindCollection(nativeInterface, owner, property);

            Property = BindProperty(nativeInterface, owner, scalar);

            Entries = BindEntries(nativeBase, nativeComplex, owner, property, original, nativeOwner);

            Ordinal = BindOrdinal(nativeComplex, complex, original, nativeElement);

            Ordinals = BindOrdinals(nativeBase, owner, nativeOwner);

            SetOrdinal = BindSetOrdinal(nativeComplex, complex, original, ordinal, nativeElement);

            State = BindState(nativeBase, owner, nativeOwner);

            SetState = BindSetState(nativeBase, owner, state, nativeOwner);

            Capacity = BindCapacity(nativeBase, owner, property, currentCount, originalCount, nativeOwner);

            Original = BindOriginal(nativeBase, owner, property, nativeOwner);

            SetModified = BindSetModified(nativeBase, owner, property, modified, nativeOwner);
        }

        /// <summary>Gets the exact member-owner infrastructure reader.</summary>
        internal Func<MemberEntry, object> Owner { get; }

        /// <summary>Gets the exact-owner complex collection wrapper constructor.</summary>
        internal Func<object, IComplexProperty, ComplexCollectionEntry> Collection { get; }

        /// <summary>Gets a scalar wrapper for original/flag recovery beneath a missing current CLR container.</summary>
        internal Func<object, IProperty, PropertyEntry> Property { get; }

        /// <summary>Gets the current/original native backing-list reader.</summary>
        internal Func<object, IComplexProperty, bool, object?> Entries { get; }

        /// <summary>Gets the validated current/original ordinal reader.</summary>
        internal Func<object, bool, int> Ordinal { get; }

        /// <summary>Gets the containing-path ordinal reader.</summary>
        internal Func<object, IReadOnlyList<int>> Ordinals { get; }

        /// <summary>Gets the validated current/original ordinal setter.</summary>
        internal Action<object, int, bool> SetOrdinal { get; }

        /// <summary>Gets the native state reader.</summary>
        internal Func<object, EntityState> State { get; }

        /// <summary>Gets the state transition preserving independent property flags.</summary>
        internal Action<object, EntityState> SetState { get; }

        /// <summary>Gets the native exact-capacity restoration operation.</summary>
        internal Action<object, IComplexProperty, int, int> Capacity { get; }

        /// <summary>Gets the native complex original-value reader.</summary>
        internal Func<object, IComplexProperty, object?> Original { get; }

        /// <summary>Gets the nonrecursive complex collection flag restoration operation.</summary>
        internal Action<object, IComplexProperty, bool> SetModified { get; }

        /// <summary>Gets the model-owned mapped setter through its validated runtime metadata contract.</summary>
        internal Func<IPropertyBase, IClrPropertySetter> Setter { get; }

        /// <summary>Gets the mapped collection factory without repeating native signature reflection.</summary>
        internal Func<IComplexProperty, int, IList> CreateCollection { get; }
    }
}
