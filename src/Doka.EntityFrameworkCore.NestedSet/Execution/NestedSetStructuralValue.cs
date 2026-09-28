namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>
/// Captures configured structural snapshots and compares metadata sentinels with default CLR semantics.
/// </summary>
internal static class NestedSetStructuralValue
{
    private static readonly ConditionalWeakTable<IProperty, ValueComparer> s_comparers = new();
    private static readonly ConditionalWeakTable<IProperty, MemberInfo> s_members = new();

    /// <summary>
    /// Reads one detached CLR, field, or property-bag value without creating retained EF entry state.
    /// </summary>
    /// <param name="entity">The detached input entity.</param>
    /// <param name="property">The mapped property metadata.</param>
    /// <returns>The current CLR value, or the configured sentinel for a shadow property.</returns>
    internal static object? Read(
        object entity,
        IProperty property
    )
    {
        if (property.IsShadowProperty())
        {
            // WHY: A detached CLR object has no shadow-state storage. Its only meaningful pre-tracking value is the
            // configured sentinel that EF itself uses to determine whether a generated value has been assigned.
            return property.Sentinel;
        }

        return property
            .GetGetter()
            .GetClrValue(entity);
    }

    /// <summary>Assigns an exact structural representation to a detached input through EF metadata.</summary>
    /// <param name="entry">The detached entity whose structural values are being staged or restored.</param>
    /// <param name="property">The mapped CLR property or backing field, validated by the nested-set mapping.</param>
    /// <param name="value">The representation to assign, independently snapshotted when mutable.</param>
    internal static void Assign(
        EntityEntry entry,
        IProperty property,
        object? value
    )
    {
        if (entry.State != EntityState.Detached)
        {
            throw new InvalidOperationException("Exact structural assignment requires a detached entity.");
        }

        if (property.IsShadowProperty()
            || property.IsIndexerProperty())
        {
            // WHY: Shadow state has no CLR member. The detached EF entry is its only storage location and is the
            // same entry that becomes Added, so the staged value participates in the eventual INSERT.
            entry.Property(property.Name)
                .CurrentValue = value;

            return;
        }

        AssignClr(entry.Entity, property, value);
    }

    /// <summary>
    /// Restores an exact detached CLR or property-bag representation without creating EF entry state.
    /// </summary>
    /// <param name="entity">The detached entity whose observable structural value is restored.</param>
    /// <param name="property">The mapped structural property.</param>
    /// <param name="value">The independently snapshotted representation.</param>
    internal static void Assign(
        object entity,
        IProperty property,
        object? value
    )
    {
        if (property.IsShadowProperty())
        {
            // WHY: Shadow values disappear with their detached EF entry and have no caller-visible CLR state
            // to restore.
            return;
        }

        AssignClr(entity, property, value);
    }

    /// <summary>
    /// Writes the mapped member directly so custom comparer equality cannot skip an exact representation.
    /// </summary>
    private static void AssignClr(
        object entity,
        IProperty property,
        object? value
    )
    {
        // WHY: EF's CurrentValue setter can skip comparer-equal values, including a defensive array copy or
        // database-distinct string. The mapped member honors field/property access and always assigns the value.
        var member = s_members.GetValue(
            property,
            static metadata => metadata.GetMemberInfo(forMaterialization: false, forSet: true));

        switch (member)
        {
            case FieldInfo field:
                field.SetValue(entity, value);

                break;
            case PropertyInfo clrProperty when property.IsIndexerProperty():
                clrProperty.SetValue(entity, value, [property.Name]);

                break;
            case PropertyInfo clrProperty:
                clrProperty.SetValue(entity, value);

                break;
            default:
                throw new InvalidOperationException("The mapped structural member cannot be assigned.");
        }
    }

    /// <summary>Uses the configured model snapshot while guaranteeing independent binary identity storage.</summary>
    /// <param name="property">The mapped structural property whose CLR representation is guarded.</param>
    /// <param name="value">The staged value, including a nullable parent key.</param>
    /// <returns>The configured model snapshot, or an independent copy of a binary structural value.</returns>
    internal static object? Snapshot(
        IProperty property,
        object? value
    )
    {
        if (value is byte[] bytes)
        {
            // WHY: Binary identities must remain owned even when an application comparer snapshots by reference.
            // Clone directly once so a configured deep array snapshot cannot introduce a second allocation.
            return bytes.Clone();
        }

        // WHY: Only the configured EF comparer knows how to copy an arbitrary mutable converted model value.
        // Default CLR snapshots retain reference types and would let caller edits change the bound identity.
        return property
            .GetValueComparer()
            .Snapshot(value);
    }

    /// <summary>Tests default CLR structural equality for metadata sentinel detection.</summary>
    /// <param name="property">The mapped property whose sentinel is checked.</param>
    /// <param name="current">The current model value.</param>
    /// <param name="expected">The configured metadata sentinel.</param>
    /// <returns>Whether the model value matches the sentinel.</returns>
    internal static bool Matches(
        IProperty property,
        object? current,
        object? expected
    ) => Comparer(property).Equals(current, expected);

    /// <summary>Shares default structural semantics only for the lifetime of the owning model metadata.</summary>
    private static ValueComparer Comparer(
            IProperty property
        )
        // WHY: Application comparers can equate database-distinct values. Sentinel checks retain default CLR
        // semantics, while snapshots use configured copy semantics and identity guards use provider equality.
        => s_comparers.GetValue(
            property,
            static metadata => ValueComparer.CreateDefault(metadata.ClrType, favorStructuralComparisons: true));
}
