namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    /// <summary>Retains one mapped container and its independently copied list membership.</summary>
    private sealed class MemberSnapshot
    {
        private readonly object?[]? _items;
        private readonly int[] _ordinals;
        private readonly IComplexProperty _property;
        private readonly object? _value;
        private object? _replacement;

        /// <summary>Retains a container without invoking a setter or accepting any pending graph change.</summary>
        internal MemberSnapshot(
            IComplexProperty property,
            object? value,
            int[] ordinals
        )
        {
            _property = property;
            _ordinals = ordinals;
            _value = value;

            if (property.IsCollection
                && value is IList list)
            {
                _items = new object?[list.Count];
                list.CopyTo(_items, 0);
            }
        }

        /// <summary>Gets the instance now holding the captured membership.</summary>
        private object? Expected => _replacement ?? _value;

        /// <summary>Distinguishes a boxed struct reference from containers with a CLR reference identity.</summary>
        private bool IsValueContainer =>
            _value is not null && _property is { IsCollection: false, ComplexType.ClrType.IsValueType: true };

        /// <summary>Restores the original list in place before restoring members nested below its ordinals.</summary>
        internal void Restore(
            object root
        )
        {
            // WHY: Unchanged read-only and immutable IList instances need no writes. Even assigning
            // the same slot would throw; a changed fixed-size array still requires positional replay.
            if (_items is not null
                && !MatchesItems((IList)Expected!))
            {
                var list = (IList)Expected!;

                if (list.IsReadOnly)
                {
                    // WHY: A read-only view can change through its hidden backing list, which recovery cannot
                    // reach. Exact membership matters more than that view's identity, so a mapped copy replaces it.
                    var copy = NestedSetComplexEntryAccess.CreateCollection(_property, _items.Length);

                    foreach (var item in _items)
                    {
                        copy.Add(item);
                    }

                    _replacement = copy;
                }
                else if (list.IsFixedSize)
                {
                    // WHY: Arrays are valid complex collections and cannot be cleared or resized.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var index = 0; index < _items.Length; index++)
                    {
                        list[index] = _items[index];
                    }
                }
                else
                {
                    list.Clear();

                    foreach (var item in _items)
                    {
                        list.Add(item);
                    }
                }
            }

            var current = _property
                .GetGetter()
                .GetClrValueUsingContainingEntity(root, _ordinals);

            if (IsValueContainer || !ReferenceEquals(current, Expected))
            {
                // WHY: Public CurrentValue setters can mark payload and compare against database originals.
                // Invoke the mapped CLR setter only for a changed reference; copying setters can replace even
                // an unchanged list. A struct has no identity to protect and its Equals is caller code that can
                // ignore members, so its box is always replayed before the comparer-owned scalar restoration.
                NestedSetComplexEntryAccess
                    .Setter(_property)
                    .SetClrValueUsingContainingEntity(root, _ordinals, Expected);

                if (_property.IsCollection)
                {
                    // WHY: A copying setter cannot keep the instance. Its copy then holds the exact membership.
                    var assigned = _property
                        .GetGetter()
                        .GetClrValueUsingContainingEntity(root, _ordinals);

                    _replacement = ReferenceEquals(assigned, _value)
                        ? null
                        : assigned;
                }
            }
        }

        /// <summary>Checks reference identity and struct container shape independently of scalar comparers.</summary>
        internal void Verify(
            object root
        )
        {
            var current = _property
                .GetGetter()
                .GetClrValueUsingContainingEntity(root, _ordinals);

            // WHY: A shallow boxed struct retains mutable scalar references. Scalar snapshots separately
            // verify their comparer-owned values; requiring those old references would reject correct replay.
            var matches = IsValueContainer
                ? current?.GetType() == _value!.GetType()
                : ReferenceEquals(current, Expected);

            if (!matches
                || (_items is not null && !MatchesItems((IList)Expected!)))
            {
                throw new InvalidOperationException("The coordinated save could not restore a complex container.");
            }
        }

        /// <summary>Checks exact positional membership, including null slots and repeated references.</summary>
        private bool MatchesItems(
            IList list
        )
        {
            if (list.Count != _items!.Length)
            {
                return false;
            }

            // WHY: Reference identity distinguishes equal-valued replacements and repeated caller objects.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _items.Length; index++)
            {
                if (!ReferenceEquals(list[index], _items[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
