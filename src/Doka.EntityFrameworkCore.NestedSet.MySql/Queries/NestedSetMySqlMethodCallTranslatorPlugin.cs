namespace Doka.EntityFrameworkCore.NestedSet.MySql;

/// <summary>Composes native hierarchy comparisons with the provider's existing SQL translators.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
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

        Translators = [new NativeScopeTranslator(sql, integerMapping), new NativePointRangeTranslator(sql)];
    }

    /// <inheritdoc />
    public IEnumerable<IMethodCallTranslator> Translators { get; }

    /// <summary>Keeps dynamic point ranges available to MariaDB for each completed source envelope.</summary>
    private sealed class NativePointRangeTranslator : IMethodCallTranslator
    {
        private readonly ISqlExpressionFactory _sql;

        /// <summary>Retains the provider SQL factory used for dynamic point-range comparisons.</summary>
        internal NativePointRangeTranslator(
            ISqlExpressionFactory sql
        )
        {
            _sql = sql;
        }

        /// <inheritdoc />
        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger
        )
        {
            if (method.DeclaringType is not { IsConstructedGenericType: true } declaringType
                || declaringType.GetGenericTypeDefinition() != typeof(NestedSetNativePointRange<>)
                || method.Name != nameof(NestedSetNativePointRange<int>.Contains)
                || arguments.Count != 2
                || arguments[0].TypeMapping is not { } mapping)
            {
                return null;
            }

            // WHY: An equality or constant prefix makes MariaDB fix a broad ref/index plan before
            // the source-derived bounds are available. Both native comparisons retain a closed
            // point while allowing dynamic range planning, including native collations/converters.
            var point = _sql.ApplyTypeMapping(arguments[1], mapping);

            return _sql.AndAlso(
                _sql.GreaterThanOrEqual(arguments[0], point),
                _sql.LessThanOrEqual(arguments[0], point));
        }
    }

    /// <summary>Preserves column collation while isolating candidate equality from constant folding.</summary>
    private sealed class NativeScopeTranslator : IMethodCallTranslator
    {
        private readonly ISqlExpressionFactory _sql;
        private readonly RelationalTypeMapping _integerMapping;

        /// <summary>Retains the SQL factory and integer mapping needed by native string comparisons.</summary>
        internal NativeScopeTranslator(
            ISqlExpressionFactory sql,
            RelationalTypeMapping integerMapping
        )
        {
            _sql = sql;
            _integerMapping = integerMapping;
        }

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
            var comparison = _sql.Function(
                "STRCMP",
                [arguments[0], _sql.ApplyTypeMapping(arguments[1], mapping)],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(int),
                _integerMapping);

            return _sql.Equal(comparison, _sql.Constant(0, _integerMapping));
        }
    }
}
