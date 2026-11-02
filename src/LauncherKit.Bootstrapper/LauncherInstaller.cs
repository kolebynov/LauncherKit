using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using LauncherKit.Infrastructure;

namespace LauncherKit.Bootstrapper;

public sealed class LauncherInstaller
{
	private static readonly TimeSpan DirectoryDeleteRetryDelay = TimeSpan.FromMilliseconds(500);

	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly ILaunchersRootDirectoryManager _launchersRootDirectoryManager;
	private readonly IInterprocessLockFactory _interprocessLockFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<LauncherInstaller> _logger;
	private readonly LauncherProductOptions _product;

	public LauncherInstaller(
		IFileSystemUtils fileSystemUtils,
		ILaunchersRootDirectoryManager launchersRootDirectoryManager,
		IInterprocessLockFactory interprocessLockFactory,
		TimeProvider timeProvider,
		ILogger<LauncherInstaller> logger, LauncherProductOptions product)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		_fileSystemUtils = fileSystemUtils;
		_launchersRootDirectoryManager = launchersRootDirectoryManager;
		_interprocessLockFactory = interprocessLockFactory;
		_timeProvider = timeProvider;
		_logger = logger;
		_product = product;
	}

	public async Task Run(NuGetVersion launcherVersion, Stream launcherArchive, CancellationToken cancellationToken)
	{
		using var writer = File.CreateText(Path.Combine(
			_launchersRootDirectoryManager.Directory,
			string.Format(CultureInfo.InvariantCulture, _launchersRootDirectoryManager.InstallLogFileNameFormat,
				_timeProvider.GetLocalNow().ToString("s", CultureInfo.InvariantCulture).Replace(':', '-'))));

		writer.AutoFlush = true;
		Console.SetOut(writer);
		Console.SetError(writer);

		try
		{
			using var interprocessLock = await _interprocessLockFactory.TryAcquire(_launchersRootDirectoryManager.Directory, cancellationToken).ConfigureAwait(false);
			if (interprocessLock == null)
			{
				_logger.LogWarning("Failed to acquire interprocess lock, aborting");
				return;
			}

			RemoveOldInstallLogs(_launchersRootDirectoryManager.Directory);

			var installedVersion = _launchersRootDirectoryManager.TryGetInstalledVersion();
			if (installedVersion != null && installedVersion >= launcherVersion)
			{
				StartLauncher(Path.Combine(_launchersRootDirectoryManager.Directory, installedVersion.ToString()));
				return;
			}

			var launcherDirectory = Path.Combine(_launchersRootDirectoryManager.Directory, launcherVersion.ToString());
			ExtractLauncher(launcherArchive, launcherDirectory);

			if (installedVersion != null)
			{
				MigrateUserSettings(Path.Combine(_launchersRootDirectoryManager.Directory, installedVersion.ToString()), launcherDirectory);
			}

			_launchersRootDirectoryManager.SetInstalledVersion(launcherVersion);
			await RemovePrevVersions(_launchersRootDirectoryManager.Directory, launcherVersion, cancellationToken).ConfigureAwait(false);
			StartLauncher(launcherDirectory);
		}
#pragma warning disable CA1031
		catch (Exception e)
#pragma warning restore CA1031
		{
			_logger.LogError(e, "Error");
			Console.ReadLine();
		}
	}

	public void ExtractLauncher(Stream launcherArchive, string launcherDirectory)
	{
		_fileSystemUtils.EnsureDirectoryExists(launcherDirectory);

		_logger.LogInformation("Extracting launcher");

		using var brotliStream = new BrotliStream(launcherArchive, CompressionMode.Decompress);
		using var tarReader = new TarReader(brotliStream);

		while (tarReader.GetNextEntry() is { } entry)
		{
			var filePath = Path.Combine(launcherDirectory, entry.Name);
			if (File.Exists(filePath))
			{
				continue;
			}

			_logger.LogInformation("Copying {FilePath}", filePath);
			using var fileStream = File.Create(filePath);
			entry.DataStream!.CopyTo(fileStream);
		}

		_logger.LogInformation("Launcher extracted");
	}

	public void MigrateUserSettings(string prevLauncherDirectory, string newLauncherDirectory)
	{
		var settingsFilePath = Path.Combine(prevLauncherDirectory, LauncherConstants.SettingsOverrideFileName);
		if (File.Exists(settingsFilePath))
		{
			_logger.LogInformation("Migrating user settings");
			File.Copy(settingsFilePath, Path.Combine(newLauncherDirectory, LauncherConstants.SettingsOverrideFileName), true);
		}
	}

	public async Task RemovePrevVersions(string launchersRootDirectory, NuGetVersion currentVersion, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Removing old versions");

		var versionString = currentVersion.ToString();
		foreach (var directory in Directory.EnumerateDirectories(launchersRootDirectory, "*.*.*", new EnumerationOptions { MatchType = MatchType.Simple }).Where(x => !x.EndsWith(versionString, StringComparison.Ordinal)))
		{
			_logger.LogInformation("Removing {Directory}", directory);
			for (var i = 0; i < 3; i++)
			{
				try
				{
					_fileSystemUtils.SafeDeleteDirectory(directory);
					break;
				}
				catch (Exception e)
				{
					_logger.LogWarning(e, "Failed to delete directory, retrying in {RetryDelay}", DirectoryDeleteRetryDelay);
					await Task.Delay(DirectoryDeleteRetryDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
				}
			}
		}
	}

	public void RemoveOldInstallLogs(string launchersRootDirectory)
	{
		_logger.LogInformation("Removing old install logs");

		var threshold = _timeProvider.GetUtcNow().AddDays(-1);
		var logsToRemove = Directory.EnumerateFiles(launchersRootDirectory, "install_*.log")
			.Where(x => new FileInfo(x).CreationTimeUtc <= threshold)
			.ToArray();
		foreach (var file in logsToRemove)
		{
			_fileSystemUtils.SafeDeleteFile(file);
		}

		_logger.LogInformation("Removed {Length} old install logs", logsToRemove.Length);
	}

	public void StartLauncher(string launcherDirectory)
	{
		var executableName = OperatingSystem.IsWindows() ? _product.AppExecutableName : Path.GetFileNameWithoutExtension(_product.AppExecutableName);
		var launcherExecutable = Path.Combine(launcherDirectory, executableName);
		Process.Start(new ProcessStartInfo(launcherExecutable)
		{
			WorkingDirectory = launcherDirectory,
			UseShellExecute = true,
		});
	}
}
