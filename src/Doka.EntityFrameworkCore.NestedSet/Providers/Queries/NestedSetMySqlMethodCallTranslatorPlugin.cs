namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Composes native MySQL Scope comparison with the provider's existing SQL translators.</summary>
internal sealed class NestedSetMySqlMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    /// <summary>Creates a translator using the provider's SQL expression and type mapping services.</summary>
    /// <param name="sql">The provider's SQL expression factory.</param>
    /// <param name="typeMappings">The provider's scalar type mapping service.</param>
    public NestedSetMySqlMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappings
    )
    {
        var integerMapping = typeMappings.FindMapping(typeof(int))
            ?? throw new InvalidOperationException("Native Scope comparison requires an integer SQL mapping.");

        Translators = [new NativeScopeTranslator(sql, integerMapping)];
    }

    /// <inheritdoc />
    public IEnumerable<IMethodCallTranslator> Translators { get; }

    /// <summary>Preserves column collation while isolating candidate equality from constant folding.</summary>
    private sealed class NativeScopeTranslator(
        ISqlExpressionFactory sql,
        RelationalTypeMapping integerMapping
    ) : IMethodCallTranslator
    {
        /// <inheritdoc />
        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger
        )
        {
            if (method.DeclaringType is not { IsConstructedGenericType: true } declaringType
                || declaringType.GetGenericTypeDefinition() != typeof(NestedSetNativeScopeEquality<>)
                || method.Name != nameof(NestedSetNativeScopeEquality<int>.Equal)
                || arguments.Count != 2
                || arguments[0].TypeMapping is not { } mapping
                || (mapping.Converter?.ProviderClrType ?? mapping.ClrType) != typeof(string))
            {
                return null;
            }

            // WHY: MySQL merges repeated column equalities and may compare their parameters under the connection
            // collation. STRCMP retains the column's native comparison, including inherited collations and padding,
            // while the separate request equality remains available for indexed lookup.
            var comparison = sql.Function(
                "STRCMP",
                [arguments[0], sql.ApplyTypeMapping(arguments[1], mapping)],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(int),
                integerMapping);

            return sql.Equal(comparison, sql.Constant(0, integerMapping));
        }
    }
}
