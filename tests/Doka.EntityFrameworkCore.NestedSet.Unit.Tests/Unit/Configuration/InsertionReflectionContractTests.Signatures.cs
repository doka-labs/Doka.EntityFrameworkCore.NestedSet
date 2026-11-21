using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionReflectionContractTests
{
    /// <summary>Notification handlers require exact argument and return types before any model can run.</summary>
    /// <param name="member">The native handler whose signature must remain qualified.</param>
    /// <param name="wrongReturn">Whether the mismatch is the return rather than the event argument.</param>
    [Theory]
    [InlineData("HandleINotifyPropertyChanging", false)]
    [InlineData("HandleINotifyPropertyChanging", true)]
    [InlineData("HandleINotifyPropertyChanged", false)]
    [InlineData("HandleINotifyPropertyChanged", true)]
    [InlineData("HandleINotifyCollectionChanged", false)]
    [InlineData("HandleINotifyCollectionChanged", true)]
    public void RequireMethod_NativeNotifications_RejectsInexactSignatures(
        string member,
        bool wrongReturn
    )
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var contract = NestedSetInsertionContract.Bind(assembly);
        var native = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalEntityEntry")!;
        var resolve = typeof(NestedSetInsertionContract).GetMethod(
            "RequireMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var arguments = member switch
        {
            "HandleINotifyPropertyChanging" => typeof(PropertyChangingEventArgs),
            "HandleINotifyPropertyChanged" => typeof(PropertyChangedEventArgs),
            "HandleINotifyCollectionChanged" => typeof(NotifyCollectionChangedEventArgs),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };

        Type[] parameters =
        [
            typeof(object), wrongReturn
                ? arguments
                : typeof(EventArgs),
        ];

        var result = wrongReturn
            ? typeof(object)
            : typeof(void);

        // Act
        var error = Record.Exception(() => resolve.Invoke(
            contract,
            BindingFlags.DoNotWrapExceptions,
            binder: null,
            [native, member, result, parameters],
            culture: null));

        // Assert
        var incompatible = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(version, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(member, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(parameters[1].FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(result.FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets", incompatible.Message, StringComparison.Ordinal);
    }

    /// <summary>Adapts test selection while the resulting delegate keeps its native target and method.</summary>
    private static Func<IUpdateEntry, Delegate> NotificationFactory(
        NestedSetInsertionContract contract,
        string kind
    ) => kind switch
    {
        "changing" => entry => contract.PropertyChangingHandler(entry),
        "changed" => entry => contract.PropertyChangedHandler(entry),
        "collection" => entry => contract.CollectionChangedHandler(entry),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Registration rejects narrowed metadata parameters and incorrect returns.</summary>
    /// <param name="member">The native relationship operation whose exact contract is checked.</param>
    /// <param name="wrongReturn">Whether the mismatch is a return type instead of a metadata parameter.</param>
    [Theory]
    [InlineData("SetRelationshipSnapshotValue", false)]
    [InlineData("SetRelationshipSnapshotValue", true)]
    [InlineData("AddToCollectionSnapshot", false)]
    [InlineData("AddToCollectionSnapshot", true)]
    [InlineData("RemoveFromCollectionSnapshot", false)]
    [InlineData("RemoveFromCollectionSnapshot", true)]
    public void RequireMethod_RelationshipAccess_RejectsInexactSignatures(
        string member,
        bool wrongReturn
    )
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var contract = NestedSetInsertionContract.Bind(assembly);
        var native = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalEntityEntry")!;
        var resolve = typeof(NestedSetInsertionContract).GetMethod(
            "RequireMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var scalar = member == "SetRelationshipSnapshotValue";
        var expectedMetadata = scalar
            ? typeof(IPropertyBase)
            : typeof(INavigationBase);

        Type[] parameters =
        [
            wrongReturn
                ? expectedMetadata
                : scalar
                    ? typeof(IProperty)
                    : typeof(INavigation),
            typeof(object),
        ];

        var result = wrongReturn
            ? typeof(object)
            : typeof(void);

        // Act
        var error = Record.Exception(() => resolve.Invoke(
            contract,
            BindingFlags.DoNotWrapExceptions,
            binder: null,
            [native, member, result, parameters],
            culture: null));

        // Assert
        var incompatible = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(version, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(member, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(parameters[0].FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(result.FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets", incompatible.Message, StringComparison.Ordinal);
    }

    /// <summary>Dependent lookup requires the exact key-vector, FK metadata and return types.</summary>
    /// <param name="wrongReturn">Whether the mismatch is the return instead of the FK metadata.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequireMethod_DependentLookup_RejectsInexactSignatures(
        bool wrongReturn
    )
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var contract = NestedSetInsertionContract.Bind(assembly);
        var type = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager")!;
        var resolve = typeof(NestedSetInsertionContract).GetMethod(
            "RequireMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Type[] parameters =
        [
            typeof(IReadOnlyList<object>), wrongReturn
                ? typeof(IForeignKey)
                : typeof(IReadOnlyForeignKey),
        ];

        var result = wrongReturn
            ? typeof(object)
            : typeof(IEnumerable<IUpdateEntry>);

        // Act
        var error = Record.Exception(() => resolve.Invoke(
            contract,
            BindingFlags.DoNotWrapExceptions,
            binder: null,
            [type, "GetDependents", result, parameters],
            culture: null));

        // Assert
        var incompatible = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(version, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("GetDependents", incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(parameters[^1].FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(result.FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets", incompatible.Message, StringComparison.Ordinal);
    }
}
