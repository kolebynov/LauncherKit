using LauncherKit.Core.Models;

namespace LauncherKit.Core.FileStorage;

public interface IFileStorageService : IDisposable
{
	FileStream? Get(string uniqueFileName);

	GetOrCreateResult GetOrCreate(string uniqueFileName, long size, Sha256Hash? hash);

	OpenedFile Allocate(string uniqueFileName, int partCount);

	/// <summary>
	/// Verifies a file that already exists at the final path. A matching hash marks the file as finished and
	/// hands out a read-only stream; a mismatch deletes the file and asks the caller to download it.
	/// </summary>
	Task<RehashResult> Rehash(
		string uniqueFileName, Func<double, ValueTask>? progressTracker, CancellationToken cancellationToken);

	/// <summary>
	/// Publishes the fully downloaded in-progress file under its final name and marks it finished.
	/// </summary>
	void Complete(string uniqueFileName);

	/// <summary>
	/// Moves partial downloads written by launchers that stored them at the final path into the
	/// in-progress path the current download flow expects, so interrupted downloads survive the upgrade.
	/// </summary>
	void MigrateLegacyPartialDownloads();

	string? GetFilePath(string uniqueFileName);

	/// <summary>Finished and partial downloads that still have a copy on disk.</summary>
	IReadOnlyList<StoredFileInfo> GetFileInfos();

	bool IsFileFinished(string uniqueFileName);

	/// <summary>
	/// Deletes the file (finished and in-progress copies) and its records. Disk failures are returned rather
	/// than suppressed, and the file stays registered when one occurs.
	/// </summary>
	DeleteResult Delete(string uniqueFileName);

	public union GetOrCreateResult(GetOrCreateResult.Existing, GetOrCreateResult.NeedRehash, GetOrCreateResult.NeedAllocate)
	{
		/// <summary>The file is stored: finished, or partially downloaded and opened to resume.</summary>
		public sealed record Existing(FileDescriptor Descriptor);

		public sealed record NeedRehash;

		public sealed record NeedAllocate;
	}

	/// <summary>Outcome of verifying a file that already exists at the final path.</summary>
	public union RehashResult(RehashResult.Finished, RehashResult.NeedAllocate)
	{
		public sealed record Finished(FileStream FileStream);

		public sealed record NeedAllocate;
	}

	/// <summary>
	/// Outcome of deleting a stored file. On failure nothing is unregistered, so the deletion can be retried;
	/// a file that is not stored counts as deleted.
	/// </summary>
	public union DeleteResult(DeleteResult.Deleted, DeleteResult.FileInUse, DeleteResult.AccessDenied, DeleteResult.Error)
	{
		public sealed record Deleted;

		/// <summary>Another process (or a running launcher task) has the file open.</summary>
		public sealed record FileInUse(string Path);

		/// <summary>The file is read-only or the user has no permission to delete it.</summary>
		public sealed record AccessDenied(string Path);

		public sealed record Error(string Path, Exception Exception);
	}
}
