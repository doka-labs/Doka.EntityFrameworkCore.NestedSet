namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Chooses whether routing follows the initial subject or each newly selected subject.</summary>
internal enum RoutingOrigin
{
    /// <summary>Retains the initial user and exact origin membership as routing owners at every hop.</summary>
    InitialSubject,

    /// <summary>Uses each selected user and its currently expanded origin membership as routing owners.</summary>
    CurrentSubject,
}
