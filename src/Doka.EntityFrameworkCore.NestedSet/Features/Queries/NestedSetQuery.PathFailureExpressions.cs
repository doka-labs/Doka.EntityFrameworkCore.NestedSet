namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    // WHY: Separate scoped/unscoped templates preserve mapped native identity joins and omit absent
    // scope roles entirely. All templates retain only expression structure and public method metadata;
    // no context, model, request, caller row or compiled delegate is held in static state.
    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string, string,
            IQueryable<long?>>> s_scopedInteriorFailureRights = (endpoints,
            failures,
            width,
            upperBlock,
            lowerBlock,
            scope,
            tree,
            left,
            right) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Scope = EF.Property<object>(child, scope),
                        Tree = EF.Property<object>(child, tree),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => new
                    {
                        group.Key.Scope,
                        group.Key.Tree,
                        group.Key.Block,
                        Right = group.Max(child => EF.Property<long>(child, right)),
                    }),
                endpoint => new
                {
                    Scope = EF.Property<object>(endpoint, scope),
                    Tree = EF.Property<object>(endpoint, tree),
                },
                block => new
                {
                    block.Scope,
                    block.Tree,
                },
                (endpoint, block) => block)
            .Select(block => block.Block > upperBlock && block.Block < lowerBlock
                ? (long?)block.Right
                : null);
    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string,
            IQueryable<long?>>> s_unscopedInteriorFailureRights = (endpoints,
            failures,
            width,
            upperBlock,
            lowerBlock,
            tree,
            left,
            right) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Tree = EF.Property<object>(child, tree),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => new
                    {
                        group.Key.Tree,
                        group.Key.Block,
                        Right = group.Max(child => EF.Property<long>(child, right)),
                    }),
                endpoint => EF.Property<object>(endpoint, tree),
                block => block.Tree,
                (endpoint, block) => block)
            .Select(block => block.Block > upperBlock && block.Block < lowerBlock
                ? (long?)block.Right
                : null);
    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string, string,
            IQueryable<long?>>> s_scopedFringeFailureRights = (endpoints,
            failures,
            width,
            upperLeft,
            lowerLeft,
            scope,
            tree,
            left,
            right) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Scope = EF.Property<object>(child, scope),
                        Tree = EF.Property<object>(child, tree),
                        Left = EF.Property<long>(child, left),
                        Right = EF.Property<long>(child, right),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => group.Key),
                endpoint => new
                {
                    Scope = EF.Property<object>(endpoint, scope),
                    Tree = EF.Property<object>(endpoint, tree),
                    Block = EF.Property<long>(endpoint, left) - EF.Property<long>(endpoint, left) % width,
                },
                point => new
                {
                    point.Scope,
                    point.Tree,
                    point.Block,
                },
                (endpoint, point) => point)
            .Select(point => point.Left > upperLeft && point.Left <= lowerLeft
                ? (long?)point.Right
                : null);
    private static readonly
        Expression<Func<IQueryable<TEntity>, IQueryable<TEntity>, long, long, long, string, string, string,
            IQueryable<long?>>> s_unscopedFringeFailureRights = (endpoints,
            failures,
            width,
            upperLeft,
            lowerLeft,
            tree,
            left,
            right) => endpoints
            .Join(
                failures
                    .GroupBy(child => new
                    {
                        Tree = EF.Property<object>(child, tree),
                        Left = EF.Property<long>(child, left),
                        Right = EF.Property<long>(child, right),
                        Block = EF.Property<long>(child, left) - EF.Property<long>(child, left) % width,
                    })
                    .Select(group => group.Key),
                endpoint => new
                {
                    Tree = EF.Property<object>(endpoint, tree),
                    Block = EF.Property<long>(endpoint, left) - EF.Property<long>(endpoint, left) % width,
                },
                point => new
                {
                    point.Tree,
                    point.Block,
                },
                (endpoint, point) => point)
            .Select(point => point.Left > upperLeft && point.Left <= lowerLeft
                ? (long?)point.Right
                : null);
}
