namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class CapacityTests
{
    /// <summary>Changes one stored field without repairing the deliberately invalid fixture row.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task<int> DamageAsync(
        IQueryable<TreeNode> target,
        string field,
        bool root,
        CancellationToken cancellationToken
    ) => field switch
    {
        nameof(TreeNode.Tree) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Tree, 2),
            cancellationToken),
        nameof(TreeNode.TreeId) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.TreeId, s_inputTreeId),
            cancellationToken),
        nameof(TreeNode.Parent) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(
                node => node.Parent,
                root
                    ? 129
                    : null),
            cancellationToken),
        nameof(TreeNode.Start) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Start, 17L),
            cancellationToken),
        nameof(TreeNode.End) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.End, node => node.End + 1),
            cancellationToken),
        nameof(TreeNode.Depth) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Depth, 9),
            cancellationToken),
        nameof(TreeNode.Position) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Position, 17L),
            cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    /// <summary>Retains structural aggregate SQL and key-range bindings for query verification.</summary>
    private sealed class CoordinateCommandProbe : DbCommandInterceptor
    {
        /// <summary>Gets the verification commands without retaining result rows or application payload.</summary>
        internal List<(string Sql, int[] Bounds, int Timeout)> Commands { get; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(
                (
                    command.CommandText,
                    command
                        .Parameters
                        .Cast<DbParameter>()
                        .Where(parameter => parameter.Value is int)
                        .Select(parameter => Convert.ToInt32(parameter.Value, CultureInfo.InvariantCulture))
                        .ToArray(),
                    command.CommandTimeout));

            return ValueTask.FromResult(result);
        }
    }
}
