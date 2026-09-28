namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Computes imported geometry by input indices before identities or native ranks are available.</summary>
    /// <param name="destination">The interval start, destination depth and existing sibling count.</param>
    /// <param name="ranks">Optional native database ranks of imported keys.</param>
    /// <param name="cancellationToken">The token checked throughout the nonrecursive traversal.</param>
    internal void PrepareGeometry(
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        IReadOnlyDictionary<TKey, int>? ranks,
        CancellationToken cancellationToken
    )
    {
        if (ranks is not null)
        {
            int Compare(int left, int right) => ranks[Nodes[left].Key].CompareTo(ranks[Nodes[right].Key]);

            Roots.Sort(Compare);
            var comparer = Comparer<int>.Create(Compare);

            foreach (var node in Nodes)
            {
                SortChildren(node, comparer);
            }
        }

        var pending = new Stack<TraversalFrame>();
        var boundary = destination.Boundary;

        for (var rootPosition = 0; rootPosition < Roots.Count; rootPosition++)
        {
            var rootIndex = Roots[rootPosition];
            var root = Nodes[rootIndex];
            root.Geometry = new Geometry(boundary, 0, destination.Depth, checked(destination.Position + rootPosition));

            boundary = checked(boundary + 1);
            pending.Push(new TraversalFrame(rootIndex, root.FirstChild, 0));

            while (pending.TryPop(out var frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = Nodes[frame.Index];

                if (frame.NextChild >= 0)
                {
                    var childIndex = frame.NextChild;
                    var child = Nodes[childIndex];
                    pending.Push(
                        frame with
                        {
                            NextChild = child.NextSibling,
                            NextPosition = checked(frame.NextPosition + 1),
                        });

                    child.Geometry = new Geometry(boundary, 0, checked(node.Geometry.Depth + 1), frame.NextPosition);

                    boundary = checked(boundary + 1);
                    pending.Push(new TraversalFrame(childIndex, child.FirstChild, 0));

                    continue;
                }

                node.Geometry = node.Geometry with { Right = boundary };
                boundary = checked(boundary + 1);
            }
        }
    }

    /// <summary>Sorts one linked child group and relinks it without retaining per-node child collections.</summary>
    private void SortChildren(
        Node parent,
        IComparer<int> comparer
    )
    {
        if (parent.ChildCount < 2)
        {
            return;
        }

        var children = new int[parent.ChildCount];
        var child = parent.FirstChild;

        for (var index = 0; index < children.Length; index++)
        {
            children[index] = child;
            child = Nodes[child].NextSibling;
        }

        Array.Sort(children, comparer);
        parent.FirstChild = children[0];
        parent.LastChild = children[^1];

        for (var index = 1; index < children.Length; index++)
        {
            Nodes[children[index - 1]].NextSibling = children[index];
        }

        Nodes[children[^1]].NextSibling = -1;
    }
}
