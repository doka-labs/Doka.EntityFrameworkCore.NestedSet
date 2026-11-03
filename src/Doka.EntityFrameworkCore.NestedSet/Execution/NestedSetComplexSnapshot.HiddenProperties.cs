namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    /// <summary>Retains scalar intent on native entries hidden by raw-null or shortened complex containers.</summary>
    private readonly struct HiddenPropertySnapshot
    {
        private readonly PropertyEntry _entry;
        private readonly IProperty _metadata;
        private readonly ValueComparer _comparer;
        private readonly object? _originalValue;
        private readonly bool _hasOriginalValue;
        private readonly bool _isModified;
        private readonly object? _elementInstance;
        private readonly object? _clrValue;
        private readonly bool _hasClr;

        /// <summary>Captures existing native scalar storage without requiring a reachable current CLR path.</summary>
        /// <param name="native">The retained native entity or complex collection element owning the scalar.</param>
        /// <param name="metadata">The mapped scalar whose original and modification intent must survive.</param>
        /// <param name="elementInstance">The independently retained CLR element, when reachable.</param>
        /// <param name="hasCurrentSlot">Whether the captured path has a legitimate current CLR element.</param>
        internal HiddenPropertySnapshot(
            object native,
            IProperty metadata,
            object? elementInstance,
            bool hasCurrentSlot
        )
        {
            _entry = NestedSetComplexEntryAccess.Property(native, metadata);
            _metadata = metadata;
            _comparer = metadata.GetValueComparer();
            _hasOriginalValue = NestedSetTrackedProperty.HasOriginalValue(metadata)
                && NestedSetComplexEntryAccess.State(native) != EntityState.Added;

            _originalValue = _hasOriginalValue ? _comparer.Snapshot(_entry.OriginalValue) : null;
            _isModified = _entry.IsModified;
            _elementInstance = elementInstance;
            var declaringInstance = ReadDeclaringInstance();
            _hasClr = hasCurrentSlot && declaringInstance is not null && !metadata.IsShadowProperty();
            _clrValue = _hasClr
                ? _comparer.Snapshot(
                    metadata
                        .GetGetter()
                        .GetClrValue(declaringInstance!))
                : null;
        }

        /// <summary>Detects changed originals, flags or captured CLR values before fast recovery.</summary>
        internal bool HasChanges =>
            _entry.IsModified != _isModified
            || (_hasOriginalValue && !_comparer.Equals(_entry.OriginalValue, _originalValue))
            || (_hasClr && !MatchesClrValue());

        /// <summary>Restores native scalar intent while the journal's temporary access graph is installed.</summary>
        internal void Restore()
        {
            RestoreOriginal();
            RestoreFlag();
            // WHY: A public flag transition can update original storage as part of EF's normal state change.
            // Retain the independent preimage after that transition without invoking another detection pass.
            RestoreOriginal();
            RestoreClrValue();
        }

        /// <summary>Writes the captured CLR value when this scalar has a reachable retained element.</summary>
        internal void RestoreClrValue()
        {
            if (_hasClr)
            {
                WriteElementScalar(_elementInstance!, _metadata, _comparer.Snapshot(_clrValue));
            }
        }

        /// <summary>Checks native intent and independent CLR storage after null containers are reinstated.</summary>
        internal void Verify()
        {
            // WHY: Hidden paths need not expose CurrentValue after final null replay. Original buffers and
            // public flags remain authoritative; an independently captured CLR instance supplies its own check.
            if (_entry.IsModified != _isModified
                || (_hasOriginalValue && !_comparer.Equals(_entry.OriginalValue, _originalValue))
                || (_hasClr && !MatchesClrValue()))
            {
                throw new InvalidOperationException(
                    $"The coordinated save could not restore hidden complex property '{_metadata.Name}'.");
            }
        }

        /// <summary>Uses EF's original-buffer snapshot writer without running public change detection.</summary>
        private void RestoreOriginal()
        {
            if (_hasOriginalValue && !_comparer.Equals(_entry.OriginalValue, _originalValue))
            {
                // WHY: EF's buffer writer already takes the property's comparer snapshot. Passing the saved
                // value avoids cloning mutable originals twice while keeping the retained preimage independent.
                NestedSetComplexOriginalValue.Restore(_entry, _originalValue);
            }
        }

        /// <summary>Restores the public modification flag without materializing a missing CLR container.</summary>
        private void RestoreFlag()
        {
            if (_entry.IsModified == _isModified)
            {
                return;
            }

            if (_isModified)
            {
                _entry.IsModified = true;

                return;
            }

            if (!_hasClr
                && _hasOriginalValue
                && !_comparer.Equals(_entry.CurrentValue, _entry.OriginalValue))
            {
                // WHY: Clearing a leaf flag normally copies its original into CLR storage. A hidden/null
                // container has no such storage. Align only its buffer while the flag is still true, reset
                // through EF's public setter, then restore the independent original without detecting changes.
                var original = _comparer.Snapshot(_entry.OriginalValue);

                try
                {
                    NestedSetComplexOriginalValue.Restore(_entry, _entry.CurrentValue);
                    _entry.IsModified = false;
                }
                finally
                {
                    NestedSetComplexOriginalValue.Restore(_entry, original);
                }

                return;
            }

            _entry.IsModified = false;
        }

        /// <summary>Compares retained storage, which a callback can have hidden behind a null reference.</summary>
        private bool MatchesClrValue() => ReadDeclaringInstance() is { } instance
            && _comparer.Equals(
                _metadata
                    .GetGetter()
                    .GetClrValue(instance),
                _clrValue);

        /// <summary>Resolves nested references anew instead of retaining an obsolete mutable struct box.</summary>
        private object? ReadDeclaringInstance() => ReadElementContainer(_elementInstance, _metadata);
    }

    /// <summary>Retains a scalar of an unregistered CLR element, which no tracker entry can restore.</summary>
    private readonly struct RawScalarSnapshot
    {
        private readonly object _element;
        private readonly IProperty _metadata;
        private readonly ValueComparer _comparer;
        private readonly object? _value;

        /// <summary>Copies one scalar value through its configured comparer.</summary>
        private RawScalarSnapshot(
            object element,
            IProperty metadata,
            object? value
        )
        {
            _element = element;
            _metadata = metadata;
            _comparer = metadata.GetValueComparer();
            _value = _comparer.Snapshot(value);
        }

        /// <summary>Journals every mapped scalar below one unregistered element.</summary>
        /// <param name="type">The complex type of <paramref name="instance"/>.</param>
        /// <param name="element">The nearest collection element, which anchors struct copyback.</param>
        /// <param name="instance">The CLR object declaring the visited members.</param>
        /// <param name="target">The frame's journal.</param>
        internal static void Capture(
            ITypeBase type,
            object element,
            object instance,
            List<RawScalarSnapshot> target
        )
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var property in type.GetProperties())
            {
                target.Add(
                    new RawScalarSnapshot(
                        element,
                        property,
                        property
                            .GetGetter()
                            .GetClrValue(instance)));
            }

            foreach (var complex in type.GetComplexProperties())
            {
                var value = complex
                    .GetGetter()
                    .GetClrValue(instance);

                if (!complex.IsCollection)
                {
                    if (value is not null)
                    {
                        Capture(complex.ComplexType, element, value, target);
                    }
                }
                else if (value is IList items)
                {
                    foreach (var item in items)
                    {
                        if (item is not null)
                        {
                            Capture(complex.ComplexType, item, item, target);
                        }
                    }
                }
            }
        }

        /// <summary>Writes the captured value back when a callback changed it.</summary>
        internal void Restore()
        {
            if (!Matches()
                && ReadElementContainer(_element, _metadata) is not null)
            {
                WriteElementScalar(_element, _metadata, _comparer.Snapshot(_value));
            }
        }

        /// <summary>Rejects a raw element whose value could not be restored.</summary>
        internal void Verify()
        {
            if (!Matches())
            {
                throw new InvalidOperationException(
                    $"The coordinated save could not restore unregistered complex property '{_metadata.Name}'.");
            }
        }

        /// <summary>Compares current storage, which a callback can have hidden behind a null reference.</summary>
        private bool Matches() => ReadElementContainer(_element, _metadata) is { } instance
            && _comparer.Equals(
                _metadata
                    .GetGetter()
                    .GetClrValue(instance),
                _value);
    }

    /// <summary>Lists the reference containers between a scalar and its collection element, outermost first.</summary>
    private static List<IComplexProperty> ElementContainers(
        IProperty metadata
    )
    {
        var containers = new List<IComplexProperty>();
        var type = metadata.DeclaringType;

        while (type is IComplexType { ComplexProperty.IsCollection: false } complex)
        {
            containers.Insert(0, complex.ComplexProperty);
            type = complex.ComplexProperty.DeclaringType;
        }

        return containers;
    }

    /// <summary>Resolves the object declaring a scalar, or null below a missing reference.</summary>
    private static object? ReadElementContainer(
        object? element,
        IProperty metadata
    )
    {
        var instance = element;

        foreach (var container in ElementContainers(metadata))
        {
            if (instance is null)
            {
                return null;
            }

            instance = container
                .GetGetter()
                .GetClrValue(instance);
        }

        return instance;
    }

    /// <summary>Writes a scalar through its element, copying back through containing structs.</summary>
    private static void WriteElementScalar(
        object element,
        IProperty metadata,
        object? value
    )
    {
        var containers = ElementContainers(metadata);

        // WHY: The element is the independent authority; a hidden element has no reliable current path, and
        // EF's containing-entity setter cannot be built through Nullable<T>. A boxed struct needs copyback,
        // so each changed container returns to its mapped member, innermost container first.
        var instances = new object[containers.Count + 1];
        instances[0] = element;

        for (var index = 0; index < containers.Count; index++)
        {
            instances[index + 1] = containers[index]
                .GetGetter()
                .GetClrValue(instances[index])!;
        }

        NestedSetTrackerSnapshot.SetMappedClrValue(instances[^1], metadata, value);

        for (var index = containers.Count - 1; index >= 0; index--)
        {
            NestedSetTrackerSnapshot.SetMappedClrValue(instances[index], containers[index], instances[index + 1]);
        }
    }
}
