namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Describes one built-in engine and its executable owner without creating a fixture resource.</summary>
internal sealed class ProviderEngineDefinition
{
    /// <summary>Retains immutable engine, marker, and executable metadata for every ownership projection.</summary>
    /// <param name="name">The exact name supplied by the built-in engine marker.</param>
    /// <param name="markerType">The built-in marker used in expected collection registrations.</param>
    /// <param name="assemblyName">The executable test assembly which owns this engine.</param>
    internal ProviderEngineDefinition(
        string name,
        Type markerType,
        string assemblyName
    )
    {
        Name = name;
        MarkerType = markerType;
        AssemblyName = assemblyName;
    }

    /// <summary>Gets the exact engine name accepted by the shared test infrastructure.</summary>
    internal string Name { get; }

    /// <summary>Gets the built-in marker type used only to describe expected fixture registrations.</summary>
    internal Type MarkerType { get; }

    /// <summary>Gets the executable test assembly which owns this engine.</summary>
    internal string AssemblyName { get; }
}
