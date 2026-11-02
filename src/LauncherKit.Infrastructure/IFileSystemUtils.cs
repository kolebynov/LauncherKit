namespace LauncherKit.Infrastructure;

public interface IFileSystemUtils
{
	void EnsureDirectoryExists(string directory, bool log = true);

	bool FileExists(string path);

	bool DirectoryExists(string path);

	/// <summary>Whether the existing directory has no files or subdirectories.</summary>
	bool IsDirectoryEmpty(string directory);

	/// <summary>Every file in the directory and its subdirectories, hidden and system files included.</summary>
	IReadOnlyList<string> GetFilesRecursive(string directory);

	/// <summary>Every subdirectory of the directory at any depth, hidden and system ones included.</summary>
	IReadOnlyList<string> GetDirectoriesRecursive(string directory);

	FileSystemUtils.SafeDeleteResult SafeDeleteDirectory(string directory);

	FileSystemUtils.SafeDeleteResult SafeDeleteFile(string filePath);

	void MoveContents(IEnumerable<string> sourceDirs, string destinationDir);

	void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);

	Task WriteAllText(string path, string contents, CancellationToken cancellationToken);

	Task<string[]> ReadAllLines(string path, CancellationToken cancellationToken);

	Task WriteAllLines(string path, IEnumerable<string> lines, CancellationToken cancellationToken);

	Task ModifyFileContent(string path, Func<string, string> modifyFunc, CancellationToken cancellationToken);

	Task ModifyFileLines(string path, Func<string[], IEnumerable<string>> modifyFunc, CancellationToken cancellationToken);

	long? GetFileSize(string path);

	string GetFullPath(string path, string basePath);

	string GetSpecialFolderPath(Environment.SpecialFolder folder);
}
