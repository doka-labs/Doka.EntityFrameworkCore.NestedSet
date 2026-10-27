namespace Doka.EntityFrameworkCore.NestedSet.Sqlite;

internal sealed partial class SqliteNestedSetProvider
{
    /// <inheritdoc />
    internal override bool UsesProjectedQueryDecision => true;

    /// <inheritdoc />
    internal override bool SupportsTrackedKeyCollection(
        IProperty key
    )
    {
        if (key.GetTypeMapping()
                .Converter is not null)
        {
            return false;
        }

        var type = key.ClrType;

        // WHY: Unsigned64 exceeds SQLite's integer domain; smaller unsigned types retain exact transport.
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(Guid);
    }
}
