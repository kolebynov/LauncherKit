using Microsoft.Extensions.Logging;

namespace LauncherKit.Infrastructure;

public class FileSystemUtils : IFileSystemUtils
{
	private static readonly EnumerationOptions RecursiveEnumeration = new()
	{
		RecurseSubdirectories = true,
		AttributesToSkip = 0,
	};

	private readonly ILogger<FileSystemUtils> _logger;

	public FileSystemUtils(ILogger<FileSystemUtils> logger)
	{
		_logger = logger;
	}

	public void EnsureDirectoryExists(string directory, bool log = true)
	{
		if (Directory.Exists(directory))
		{
			return;
		}

		if (log)
		{
			_logger.LogInformation("Creating directory {Directory}", directory);
		}

		Directory.CreateDirectory(directory);
	}

	public bool FileExists(string path) => File.Exists(path);

	public bool DirectoryExists(string path) => Directory.Exists(path);

	public bool IsDirectoryEmpty(string directory) => !Directory.EnumerateFileSystemEntries(directory).Any();

	public IReadOnlyList<string> GetFilesRecursive(string directory) =>
		Directory.GetFiles(directory, "*", RecursiveEnumeration);

	public IReadOnlyList<string> GetDirectoriesRecursive(string directory) =>
		Directory.GetDirectories(directory, "*", RecursiveEnumeration);

	public SafeDeleteResult SafeDeleteDirectory(string directory)
	{
		_logger.LogInformation("Deleting directory {Directory}", directory);
		var result = DeleteImpl();
		_logger.LogInformation("Directory {Directory} deletion result {Result}", directory, result.Value);

		return result;

		SafeDeleteResult DeleteImpl()
		{
			try
			{
				if (!Directory.Exists(directory))
				{
					return SafeDeleteResult.NotExist.Instance;
				}

				Directory.Delete(directory, true);
				return SafeDeleteResult.Success.Instance;
			}
			catch (Exception e)
			{
				return new SafeDeleteResult.Error(e);
			}
		}
	}

	public SafeDeleteResult SafeDeleteFile(string filePath)
	{
		_logger.LogInformation("Deleting file {File}", filePath);
		var result = DeleteImpl();
		_logger.LogInformation("File {File} deletion result {Result}", filePath, result.Value);

		return result;

		SafeDeleteResult DeleteImpl()
		{
			try
			{
				if (!File.Exists(filePath))
				{
					return SafeDeleteResult.NotExist.Instance;
				}

				File.Delete(filePath);
				return SafeDeleteResult.Success.Instance;
			}
			catch (Exception e)
			{
				return new SafeDeleteResult.Error(e);
			}
		}
	}

	public void MoveContents(IEnumerable<string> sourceDirs, string destinationDir)
	{
		EnsureDirectoryExists(destinationDir);

		var fileEnumerationOptions = new EnumerationOptions
		{
			IgnoreInaccessible = true,
			MatchType = MatchType.Simple,
			RecurseSubdirectories = true,
		};
		var createdDirs = new HashSet<string> { destinationDir };

		var sourceDirsList = sourceDirs.ToList();
		_logger.LogInformation("Moving contents from {SourceDirs} to {DestinationDir}", string.Join(", ", sourceDirsList), destinationDir);

		foreach (var sourceDir in sourceDirsList)
		{
			if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
			{
				continue;
			}

			foreach (var file in Directory.EnumerateFiles(sourceDir, "*", fileEnumerationOptions))
			{
				var relativePath = Path.GetRelativePath(sourceDir, file);
				var targetPath = Path.Combine(destinationDir, relativePath);
				var targetDir = Path.GetDirectoryName(targetPath)!;
				if (createdDirs.Add(targetDir))
				{
					EnsureDirectoryExists(targetDir, false);
				}

				MoveFile(file, targetPath, true);
			}
		}
	}

	public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
		File.Move(sourcePath, destinationPath, overwrite);

	public Task WriteAllText(string path, string contents, CancellationToken cancellationToken) =>
		File.WriteAllTextAsync(path, contents, cancellationToken);

	public Task<string[]> ReadAllLines(string path, CancellationToken cancellationToken) =>
		File.ReadAllLinesAsync(path, cancellationToken);

	public Task WriteAllLines(string path, IEnumerable<string> lines, CancellationToken cancellationToken) =>
		File.WriteAllLinesAsync(path, lines, cancellationToken);

	public async Task ModifyFileContent(string path, Func<string, string> modifyFunc, CancellationToken cancellationToken)
	{
		var content = await File.ReadAllTextAsync(path, cancellationToken);
		var modifiedContent = modifyFunc(content);
		await File.WriteAllTextAsync(path, modifiedContent, cancellationToken);
	}

	public async Task ModifyFileLines(string path, Func<string[], IEnumerable<string>> modifyFunc, CancellationToken cancellationToken)
	{
		var lines = await File.ReadAllLinesAsync(path, cancellationToken);
		var modifiedContent = modifyFunc(lines);
		await File.WriteAllLinesAsync(path, modifiedContent, cancellationToken);
	}

	public long? GetFileSize(string path)
	{
		try
		{
			return new FileInfo(path).Length;
		}
		catch (FileNotFoundException)
		{
			return null;
		}
	}

	public string GetFullPath(string path, string basePath) => Path.GetFullPath(path, basePath);

	public string GetSpecialFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

	public union SafeDeleteResult(SafeDeleteResult.Success, SafeDeleteResult.NotExist, SafeDeleteResult.Error)
	{
		public sealed record Success;

		public sealed record NotExist;

		public sealed record Error(Exception Exception);
	}
}