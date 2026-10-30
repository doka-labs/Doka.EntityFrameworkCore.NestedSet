using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportBuilder
{
    private static readonly ParameterExpression s_definition = Expression.Parameter(typeof(TagDefinition), "field");

    private static readonly MemberExpression s_definitionType = Expression.Property(
        s_definition,
        nameof(TagDefinition.TypeId));

    private static readonly MemberExpression s_definitionCode = Expression.Property(
        s_definition,
        nameof(TagDefinition.FieldCode));

    private static readonly ParameterExpression s_choice = Expression.Parameter(typeof(TagChoice), "choice");
    private static readonly MemberExpression s_choiceType = Expression.Property(s_choice, nameof(TagChoice.TypeId));

    private static readonly MemberExpression s_choiceCode = Expression.Property(s_choice, nameof(TagChoice.FieldCode));

    private static readonly MemberExpression s_choiceValue = Expression.Property(s_choice, nameof(TagChoice.Value));

    /// <summary>Projects authorized requested anchors, channels, definitions and exact choices in one reader.</summary>
    internal static IQueryable<SignalCatalogRow> Catalog(
        SignalLabContext context,
        ValidatedSignalReport request
    )
    {
        var tenant = request.TenantId;
        var anchors = request.AnchorIds;
        var channels = request.ChannelIds;
        var tree = context
            .NestedSet<SignalNode>()
            .ForScope(tenant)
            .InTree(request.TreeId);

        var (expression, choices) = RequestedCatalogBindings(request);
        var definitions = context.SignalChannels.Where(channel => channel.TenantId == tenant);

        // WHY: Match the report's SQLite collection policy without changing provider-wide EF options.
        var singleCollectionParameter = context.Database.IsSqlite();
        var requestedAnchors = singleCollectionParameter
            ? tree.Nodes.Where(node => Enumerable.Contains(EF.Parameter(anchors), node.Id))
            : tree.Nodes.Where(node => Enumerable.Contains(anchors, node.Id));

        var requestedDefinitions = singleCollectionParameter
            ? definitions.Where(channel => Enumerable.Contains(EF.Parameter(channels), channel.Id))
            : definitions.Where(channel => Enumerable.Contains(channels, channel.Id));

        return requestedAnchors
            .Select(CatalogProjections.Anchor)
            .Concat(requestedDefinitions.Select(CatalogProjections.Channel))
            .Concat(
                context
                    .TagDefinitions
                    .Where(field => field.TenantId == tenant)
                    .Where(expression)
                    .Select(CatalogProjections.Definition))
            .Concat(
                context
                    .TagChoices
                    .Where(choice => choice.TenantId == tenant)
                    .Where(choices)
                    .Select(CatalogProjections.Choice));
    }

    /// <summary>Builds parameterized field/value pairs without reading an entire classification's catalog.</summary>
    private static (Expression<Func<TagDefinition, bool>> Definitions, Expression<Func<TagChoice, bool>> Choices)
        RequestedCatalogBindings(
            ValidatedSignalReport request
        )
    {
        var definitions = new Expression[request.Groups.Length];
        var choices = new Expression[request.Groups.Length];

        // WHY: Fill parallel definition and choice expression buffers at the same requested-group position.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var groupIndex = 0; groupIndex < request.Groups.Length; groupIndex++)
        {
            var group = request.Groups[groupIndex];
            var type = RequestedGuid(group.TypeId);
            var fields = new Expression[group.Criteria.Length];
            var pairs = new Expression[group.Criteria.Length];

            // WHY: Build field and field/value expressions from the same criterion position.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < group.Criteria.Length; index++)
            {
                var requested = group.Criteria[index];
                var criterion = Expression.Constant(new CriterionParameters(requested.FieldCode, requested.Value));
                var field = Expression.Field(criterion, s_requestedFieldCode);
                var value = Expression.Field(criterion, s_requestedValue);
                fields[index] = Expression.Equal(s_definitionCode, field, false, s_textEquality);
                pairs[index] = Expression.AndAlso(
                    Expression.Equal(s_choiceCode, field, false, s_textEquality),
                    Expression.Equal(s_choiceValue, value, false, s_textEquality));
            }

            definitions[groupIndex] = Expression.AndAlso(
                Expression.Equal(s_definitionType, type, false, s_guidEquality),
                Alternatives(fields, 0, fields.Length));

            choices[groupIndex] = Expression.AndAlso(
                Expression.Equal(s_choiceType, type, false, s_guidEquality),
                Alternatives(pairs, 0, pairs.Length));
        }

        // WHY: Match each requested field/value together. Independent IN lists would admit crossed pairs.
        // SQL comparison may overmatch padded aliases; ordinal post-read validation still fails them closed.
        return (
            Expression.Lambda<Func<TagDefinition, bool>>(
                Alternatives(definitions, 0, definitions.Length),
                s_definition),
            Expression.Lambda<Func<TagChoice, bool>>(Alternatives(choices, 0, choices.Length), s_choice));
    }

    /// <summary>Combines alternatives with logarithmic depth without imposing a new criterion-count limit.</summary>
    private static Expression Alternatives(
        Expression[] alternatives,
        int start,
        int count
    )
    {
        if (count == 0)
        {
            return Expression.Constant(false);
        }

        if (count == 1)
        {
            return alternatives[start];
        }

        var half = count / 2;

        return Expression.OrElse(
            Alternatives(alternatives, start, half),
            Alternatives(alternatives, start + half, count - half));
    }

    /// <summary>Owns fixed, context-free projections used only by catalog composition.</summary>
    private static class CatalogProjections
    {
        // WHY: The separate initialization boundary keeps Build-only callers from initializing catalog graphs.
        // Each catalog query still binds its own context, authorization and request scalars outside these templates.

        /// <summary>Projects an authorized anchor identity and its hierarchy classification.</summary>
        internal static readonly Expression<Func<SignalNode, SignalCatalogRow>> Anchor = node => new()
        {
            Kind = 0,
            Id = node.Id,
            TypeId = node.TypeId,
            FieldCode = "",
            Value = "",
            UnitCode = "",
        };

        /// <summary>Projects an authorized channel identity, measurement classification and unit.</summary>
        internal static readonly Expression<Func<SignalChannel, SignalCatalogRow>> Channel = channel => new()
        {
            Kind = 1,
            Id = channel.Id,
            TypeId = channel.MeasurementTypeId,
            FieldCode = "",
            Value = "",
            UnitCode = channel.UnitCode,
        };

        /// <summary>Projects one requested metadata field definition.</summary>
        internal static readonly Expression<Func<TagDefinition, SignalCatalogRow>> Definition = field => new()
        {
            Kind = 2,
            Id = Guid.Empty,
            TypeId = field.TypeId,
            FieldCode = field.FieldCode,
            Value = "",
            UnitCode = "",
        };

        /// <summary>Projects one requested exact field and permitted scalar choice.</summary>
        internal static readonly Expression<Func<TagChoice, SignalCatalogRow>> Choice = choice => new()
        {
            Kind = 3,
            Id = Guid.Empty,
            TypeId = choice.TypeId,
            FieldCode = choice.FieldCode,
            Value = choice.Value,
            UnitCode = "",
        };
    }
}
