namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Exposes the existing immutable adapter after validation in the actual EF context scope.</summary>
internal interface INestedSetProviderBinding
{
    /// <summary>Gets the adapter whose options, native provider and service identities have been validated.</summary>
    NestedSetProviderCapabilities Provider { get; }
}
