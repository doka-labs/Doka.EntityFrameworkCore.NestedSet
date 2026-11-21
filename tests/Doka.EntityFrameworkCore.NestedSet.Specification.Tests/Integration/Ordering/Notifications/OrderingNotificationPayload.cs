namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents pending notification-tracked application data unrelated to hierarchy coordinates.</summary>
public sealed class OrderingNotificationPayload : OrderingNotificationEntity
{
    private int _id;
    private string _value = "";

    /// <summary>Gets or sets the application-assigned identity.</summary>
    public int Id
    {
        get => _id;
        set => SetValue(ref _id, value, nameof(Id));
    }

    /// <summary>Gets or sets an ordinary scalar for which EF does not retain an original value.</summary>
    public string Value
    {
        get => _value;
        set => SetValue(ref _value, value, nameof(Value));
    }
}
