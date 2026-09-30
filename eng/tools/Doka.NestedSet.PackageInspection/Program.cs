namespace Doka.NestedSet.PackageInspection;

/// <summary>Verifies candidate PE/PDB metadata and emits the exact public symbol-server probe.</summary>
internal static class Program
{
    /// <summary>Reads package bytes asynchronously and returns failure for incomplete or mismatched metadata.</summary>
    private static async Task<int> Main(
        string[] args
    )
    {
        if (args.Length != 4)
        {
            await Console.Error.WriteLineAsync(
                "Usage: PackageInspection <nupkg> <snupkg> <package-id> <package-version>".AsMemory(),
                CancellationToken.None);

            return 1;
        }

        try
        {
            var assemblyName = $"lib/net10.0/{args[2]}.dll";
            var pdbName = $"{args[2]}.pdb";
            var assembly = await ReadEntryAsync(args[0], assemblyName, CancellationToken.None);
            var pdb = await ReadEntryAsync(args[1], $"lib/net10.0/{pdbName}", CancellationToken.None);
            var probe = Verify(assembly, pdb, assemblyName, pdbName, args[2], args[3]);

            await Console.Out.WriteLineAsync(
                JsonSerializer
                    .Serialize(probe)
                    .AsMemory(),
                CancellationToken.None);

            return 0;
        }
        catch (Exception exception) when (exception is IOException
                                              or InvalidDataException
                                              or BadImageFormatException
                                              or ArgumentException)
        {
            await Console.Error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None);

            return 1;
        }
    }

    /// <summary>Validates assembly release identity, CodeView GUID, and the deterministic Portable PDB checksum.</summary>
    private static object Verify(
        byte[] assembly,
        byte[] pdb,
        string assemblyName,
        string pdbName,
        string packageId,
        string packageVersion
    )
    {
        using var assemblyStream = new MemoryStream(assembly, writable: false);
        using var peReader = new PEReader(assemblyStream);
        var reader = peReader.GetMetadataReader();

        if (reader.GetString(reader.GetAssemblyDefinition().Name) != packageId)
        {
            throw new InvalidDataException("Assembly identity does not match its package.");
        }

        ValidateInformationalVersion(reader, packageVersion);

        var entries = peReader.ReadDebugDirectory();
        var codeViews = entries
            .Where(entry => entry is { Type: DebugDirectoryEntryType.CodeView, IsPortableCodeView: true })
            .ToArray();

        var checksums = entries
            .Where(entry => entry.Type == DebugDirectoryEntryType.PdbChecksum)
            .ToArray();

        if (codeViews.Length != 1
            || checksums.Length != 1)
        {
            throw new InvalidDataException("Assembly must contain one Portable CodeView identity and checksum.");
        }

        var codeView = peReader.ReadCodeViewDebugDirectoryData(codeViews[0]);
        var checksum = peReader.ReadPdbChecksumDebugDirectoryData(checksums[0]);

        if (Path.GetFileName(codeView.Path.Replace('\\', '/')) != pdbName
            || !StringComparer.OrdinalIgnoreCase.Equals(checksum.AlgorithmName, "SHA256"))
        {
            throw new InvalidDataException("Assembly identifies unexpected symbols or checksum algorithm.");
        }

        if (!peReader.TryOpenAssociatedPortablePdb(
                assemblyName,
                _ => new MemoryStream(pdb, writable: false),
                out var provider,
                out _)
            || provider is null)
        {
            throw new InvalidDataException("Portable PDB does not match the candidate assembly.");
        }

        using (provider)
        {
            var header = provider.GetMetadataReader().DebugMetadataHeader;

            if (header is null
                || new BlobContentId(header.Id).Guid != codeView.Guid
                || header.Id.Length != 20
                || header.IdStartOffset < 0
                || header.IdStartOffset > pdb.Length - header.Id.Length)
            {
                throw new InvalidDataException("Portable PDB metadata identity is invalid.");
            }

            // WHY: The deterministic checksum zeros the 20-byte content ID to avoid hashing its own hash.
            // Hash segments directly so verification does not allocate a second full PDB buffer.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(pdb.AsSpan(0, header.IdStartOffset));
            hash.AppendData(new byte[20]);
            hash.AppendData(pdb.AsSpan(header.IdStartOffset + header.Id.Length));

            if (!hash
                    .GetHashAndReset()
                    .AsSpan()
                    .SequenceEqual(checksum.Checksum.AsSpan()))
            {
                throw new InvalidDataException("Portable PDB checksum does not match the candidate assembly.");
            }
        }

        // WHY: SSQP uses UInt32.MaxValue as the Portable PDB age; the raw file hash is a separate readback check.
        var key = $"{codeView.Guid:N}FFFFFFFF";

        return new
        {
            pdbName,
            url = $"https://symbols.nuget.org/download/symbols/{pdbName}/{key}/{pdbName}",
            checksum = $"SHA256:{Convert.ToHexString(checksum.Checksum.AsSpan()).ToLowerInvariant()}",
            sha256 = Convert
                .ToHexString(SHA256.HashData(pdb))
                .ToLowerInvariant(),
        };
    }

    /// <summary>Checks the release identity in metadata without loading or executing the candidate assembly.</summary>
    private static void ValidateInformationalVersion(
        MetadataReader reader,
        string expectedVersion
    )
    {
        string? informationalVersion = null;
        var attributes = reader
            .GetAssemblyDefinition()
            .GetCustomAttributes();

        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);

            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);

            if (constructor.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);

            if (reader.GetString(type.Namespace) != "System.Reflection"
                || reader.GetString(type.Name) != "AssemblyInformationalVersionAttribute")
            {
                continue;
            }

            if (informationalVersion is not null)
            {
                throw new InvalidDataException("Assembly contains duplicate informational versions.");
            }

            var value = reader.GetBlobReader(attribute.Value);

            if (value.ReadUInt16() != 1)
            {
                throw new InvalidDataException("Assembly informational version metadata is invalid.");
            }

            informationalVersion = value.ReadSerializedString();

            if (informationalVersion is null
                || value.ReadUInt16() != 0
                || value.RemainingBytes != 0)
            {
                throw new InvalidDataException("Assembly informational version metadata is invalid.");
            }
        }

        // WHY: Numeric AssemblyVersion may remain stable for binding compatibility; this value identifies the release.
        // The SDK can append a full source SHA, so accept that suffix while requiring the exact selected version.
        if (informationalVersion is null
            || !MatchesInformationalVersion(informationalVersion, expectedVersion))
        {
            throw new InvalidDataException("Assembly informational version does not match its package.");
        }
    }

    /// <summary>Accepts the selected release version with only the SDK's optional full source-commit suffix.</summary>
    private static bool MatchesInformationalVersion(
        string actualVersion,
        string expectedVersion
    )
    {
        if (StringComparer.Ordinal.Equals(actualVersion, expectedVersion))
        {
            return true;
        }

        if (actualVersion.Length != expectedVersion.Length + 41
            || !actualVersion.StartsWith(expectedVersion, StringComparison.Ordinal)
            || actualVersion[expectedVersion.Length] != '+')
        {
            return false;
        }

        for (var index = expectedVersion.Length + 1; index < actualVersion.Length; index++)
        {
            if (!Uri.IsHexDigit(actualVersion[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the unique expected package entry without extracting any archive path.</summary>
    private static async Task<byte[]> ReadEntryAsync(
        string packagePath,
        string entryName,
        CancellationToken cancellationToken
    )
    {
        await using var file = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        await using var archive = await ZipArchive.CreateAsync(
            file,
            ZipArchiveMode.Read,
            leaveOpen: true,
            entryNameEncoding: null,
            cancellationToken);

        var entries = archive
            .Entries
            .Where(entry => StringComparer.Ordinal.Equals(entry.FullName, entryName))
            .ToArray();

        if (entries.Length != 1)
        {
            throw new InvalidDataException($"Expected one {entryName} in {packagePath}.");
        }

        await using var source = await entries[0]
            .OpenAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
