using LauncherKit.Core.Models;

namespace LauncherKit.Core.Services.Abstractions;

public interface ISevenZipService
{
	Task ExtractArchive(string archivePath, string destinationDirectory, CancellationToken cancellationToken);

	/// <summary>
	/// Extracts the whole archive except the entry <paramref name="excludedEntryPath"/> (its path in the archive), e.g. a
	/// version file that must change only after the following install steps succeed.
	/// </summary>
	Task ExtractArchiveExcept(string archivePath, string destinationDirectory, string excludedEntryPath,
		CancellationToken cancellationToken);

	Task ExtractEntriesFromArchive(IEnumerable<string> entryPathsInArchive, string archivePath,
		string destinationDirectory, bool preserveFullPath, CancellationToken cancellationToken);

	Task<bool> ContainsEntry(string archivePath, string entryPathInArchive, CancellationToken cancellationToken);

	/// <summary>Every file and directory entry of the archive, with paths relative to the archive root.</summary>
	IAsyncEnumerable<ArchiveEntry> ListArchiveEntries(string archivePath, CancellationToken cancellationToken);
}
