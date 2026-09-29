namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Retains generated anchor identities for one independently initialized demonstration forest.</summary>
/// <param name="TreeId">The main tree identity owned by the scenario.</param>
/// <param name="ArchiveTreeId">The independent archive tree identity.</param>
/// <param name="Root">The main root.</param>
/// <param name="Documents">The user-owned branch beneath the root.</param>
/// <param name="Projects">The nearest system-category ancestor of the repository.</param>
/// <param name="Repository">The leaf used for anchor queries and subtree moves.</param>
/// <param name="Shared">The other branch available as a destination.</param>
/// <param name="ArchiveRoot">The root of the independent archive tree.</param>
internal sealed record FolderForest(
    Guid TreeId,
    Guid ArchiveTreeId,
    Folder Root,
    Folder Documents,
    Folder Projects,
    Folder Repository,
    Folder Shared,
    Folder ArchiveRoot
)
{
    /// <summary>Initializes two trees through the same public operations used by an application.</summary>
    /// <param name="context">The fresh unit of work for this scenario.</param>
    /// <param name="treeId">The never-before-used main tree identity.</param>
    /// <param name="archiveTreeId">The never-before-used archive tree identity.</param>
    /// <param name="cancellationToken">The token for every asynchronous database operation.</param>
    /// <returns>The persisted anchors; inserted entities are detached by the public facade.</returns>
    internal static async Task<FolderForest> CreateAsync(
        FileSystemContext context,
        Guid treeId,
        Guid archiveTreeId,
        CancellationToken cancellationToken
    )
    {
        var folders = context.NestedSet<Folder>();
        var root = new Folder("System", "System");
        var documents = new Folder("Documents");
        var projects = new Folder("Projects", "System");
        var repository = new Folder("NestedSet");
        var shared = new Folder("Shared");
        var archiveRoot = new Folder("Archive");

        await folders.InsertRootAsync(root, treeId, cancellationToken);

        // WHY: Reverse input order makes the configured alphabetic placement observable immediately.
        await folders.InsertChildAsync(shared, root.Id, cancellationToken);
        await folders.InsertChildAsync(documents, root.Id, cancellationToken);
        await folders.InsertChildAsync(projects, documents.Id, cancellationToken);
        await folders.InsertChildAsync(new Folder("Notes"), projects.Id, cancellationToken);
        await folders.InsertChildAsync(repository, projects.Id, cancellationToken);
        await folders.InsertChildAsync(new Folder("Public"), shared.Id, cancellationToken);

        await folders.InsertRootAsync(archiveRoot, archiveTreeId, cancellationToken);
        await folders.InsertChildAsync(new Folder("NestedSet"), archiveRoot.Id, cancellationToken);

        return new FolderForest(
            treeId,
            archiveTreeId,
            root,
            documents,
            projects,
            repository,
            shared,
            archiveRoot);
    }
}
