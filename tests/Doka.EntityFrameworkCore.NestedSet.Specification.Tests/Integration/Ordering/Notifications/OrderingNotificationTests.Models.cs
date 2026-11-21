using System.ComponentModel;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Supplies real change notifications without retaining originals for ordinary scalar properties.</summary>
public abstract class OrderingNotificationEntity : INotifyPropertyChanging,
    INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event PropertyChangingEventHandler? PropertyChanging;

    /// <summary>Raises the paired events required by EF's changing-and-changed notification strategy.</summary>
    /// <typeparam name="TValue">The mapped CLR property type.</typeparam>
    /// <param name="field">The backing storage changed by the domain setter.</param>
    /// <param name="value">The new domain value.</param>
    /// <param name="name">The mapped property name reported to EF.</param>
    protected void SetValue<TValue>(
        ref TValue field,
        TValue value,
        string name
    )
    {
        if (EqualityComparer<TValue>.Default.Equals(field, value))
        {
            return;
        }

        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(name));
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
