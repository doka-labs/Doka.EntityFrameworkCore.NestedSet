namespace Doka.EntityFrameworkCore.NestedSet.SqlServer;

internal sealed partial class SqlServerNestedSetProvider
{
    /// <inheritdoc />
    internal override string AncestorFlagSql(
        string predicate
    ) => $"CONVERT(bit, CASE WHEN {predicate} THEN 1 ELSE 0 END)";

    /// <inheritdoc />
    internal override bool IsCompatibleIndexAnnotation(
        IConventionAnnotation annotation
    ) => annotation.Name switch
    {
        "SqlServer:Include" => annotation.Value is string[],
        "SqlServer:Clustered" or "SqlServer:Online" => annotation.Value is bool,
        "SqlServer:FillFactor" => annotation.Value is int and >= 1 and <= 100,
        "SqlServer:SortInTempDb" => annotation.Value is bool,
        _ => false,
    };
}
