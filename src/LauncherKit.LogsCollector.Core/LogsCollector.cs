using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using LauncherKit.Infrastructure;

namespace LauncherKit.LogsCollector.Core;

public sealed class LogsCollector : ILogsCollector
{
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly ILaunchersRootDirectoryManager _launchersRootDirectoryManager;
	private readonly ILogger<LogsCollector> _logger;
	private readonly TimeProvider _timeProvider;

	public LogsCollector(IFileSystemUtils fileSystemUtils, ILaunchersRootDirectoryManager launchersRootDirectoryManager,
		ILogger<LogsCollector> logger, TimeProvider timeProvider)
	{
		_fileSystemUtils = fileSystemUtils;
		_launchersRootDirectoryManager = launchersRootDirectoryManager;
		_logger = logger;
		_timeProvider = timeProvider;
	}

	public Task<Exception?> CollectLogs(string outputFileDirectory, string? modOrganizerDirectory, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(outputFileDirectory);

		return new Collector(_fileSystemUtils, _launchersRootDirectoryManager, _logger, _timeProvider, outputFileDirectory,
			modOrganizerDirectory, cancellationToken).Collect();
	}

	private sealed class Collector
	{
		private const int MaxExternalLogFiles = 10;

		private readonly IFileSystemUtils _fileSystemUtils;
		private readonly ILaunchersRootDirectoryManager _launchersRootDirectoryManager;
		private readonly LogsCollectingLogger _logger;
		private readonly string _outputFileDirectory;
		private readonly string? _modOrganizerDirectory;
		private readonly CancellationToken _cancellationToken;

		public Collector(IFileSystemUtils fileSystemUtils, ILaunchersRootDirectoryManager launchersRootDirectoryManager,
			ILogger logger, TimeProvider timeProvider, string outputFileDirectory, string? modOrganizerDirectory,
			CancellationToken cancellationToken)
		{
			_fileSystemUtils = fileSystemUtils;
			_launchersRootDirectoryManager = launchersRootDirectoryManager;
			_logger = new LogsCollectingLogger(logger, timeProvider);
			_outputFileDirectory = outputFileDirectory;
			_modOrganizerDirectory = modOrganizerDirectory;
			_cancellationToken = cancellationToken;
		}

		public async Task<Exception?> Collect()
		{
			ZipArchive? logsZipArchive = null;

			try
			{
				var logsArchivePath = Path.Combine(_outputFileDirectory, "collected_logs.zip");
				_logger.LogInformation(
					"Start collecting logs from {LaunchersRootDirectory} to {LogsArchivePath}",
					_launchersRootDirectoryManager.Directory, logsArchivePath);

				_fileSystemUtils.EnsureDirectoryExists(_outputFileDirectory);
				logsZipArchive = new ZipArchive(File.OpenWrite(logsArchivePath), ZipArchiveMode.Create);

				await AddLauncherLogs(logsZipArchive);
				await AddExternalLogs(logsZipArchive, GetSkseLogsDirectory(), "skse");
				if (!string.IsNullOrEmpty(_modOrganizerDirectory))
				{
					await AddExternalLogs(logsZipArchive, Path.Combine(_modOrganizerDirectory, "logs"), "mod-organizer");
				}

				return null;
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Failed to collect logs");
				return e;
			}
			finally
			{
				if (logsZipArchive != null)
				{
					using var _ = logsZipArchive;
					var collectorLogsEntry = logsZipArchive.CreateEntry("logs-collector.log");
					using var collectorLogsWriter = new StreamWriter(await collectorLogsEntry.OpenAsync(_cancellationToken), Encoding.UTF8);
					await collectorLogsWriter.WriteAsync(_logger.LogsBuilder, _cancellationToken);
				}
			}
		}

		private async Task AddLauncherLogs(ZipArchive logsZipArchive)
		{
			await AddLogEntries(logsZipArchive, Directory.EnumerateFiles(_launchersRootDirectoryManager.Directory),
				_launchersRootDirectoryManager.Directory, string.Empty);

			var installedLauncherVersion = _launchersRootDirectoryManager.TryGetInstalledVersion();
			_logger.LogInformation("Detected launcher version {LauncherVersion}", installedLauncherVersion);
			if (installedLauncherVersion == null)
			{
				return;
			}

			var launcherDirectory = Path.Combine(_launchersRootDirectoryManager.Directory,
				installedLauncherVersion.ToString());
			if (!Directory.Exists(launcherDirectory))
			{
				_logger.LogWarning("Launcher directory {LauncherDirectory} does not exist", launcherDirectory);
				return;
			}

			await AddLogEntries(logsZipArchive, Directory.EnumerateFiles(launcherDirectory, "appsettings.*"),
				_launchersRootDirectoryManager.Directory, string.Empty);
			await AddLogEntries(logsZipArchive, [Path.Combine(launcherDirectory, "console.log")],
				_launchersRootDirectoryManager.Directory, string.Empty);
			var logsDirectory = Path.Combine(launcherDirectory, "logs");
			if (Directory.Exists(logsDirectory))
			{
				await AddLogEntries(logsZipArchive, Directory.EnumerateFiles(logsDirectory),
					_launchersRootDirectoryManager.Directory, string.Empty);
			}
		}

		private string GetSkseLogsDirectory() =>
			Path.Combine(_fileSystemUtils.GetSpecialFolderPath(Environment.SpecialFolder.MyDocuments),
				"My Games", "Skyrim Special Edition", "SKSE");

		private async Task AddExternalLogs(ZipArchive logsZipArchive, string directory, string entryPrefix)
		{
			if (!Directory.Exists(directory))
			{
				_logger.LogWarning("Logs directory {Directory} does not exist", directory);
				return;
			}

			var files = new DirectoryInfo(directory)
				.EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
				.OrderByDescending(x => x.LastWriteTimeUtc)
				.Take(MaxExternalLogFiles)
				.Select(x => x.FullName)
				.ToList();
			await AddLogEntries(logsZipArchive, files, directory, entryPrefix);
		}

		private async Task AddLogEntries(ZipArchive logsZipArchive, IEnumerable<string> files, string baseDirectory,
			string entryPrefix)
		{
			foreach (var file in files)
			{
				_logger.LogInformation("Adding {File} to logs archive", file);
				if (!File.Exists(file))
				{
					_logger.LogWarning("File {File} does not exist", file);
					continue;
				}

				try
				{
					using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
					var relativePath = Path.GetRelativePath(baseDirectory, file);
					var entryName = string.IsNullOrEmpty(entryPrefix) ? relativePath : Path.Combine(entryPrefix, relativePath);
					var entry = logsZipArchive.CreateEntry(entryName);
					using var entryStream = await entry.OpenAsync(_cancellationToken);
					await fs.CopyToAsync(entryStream, 4096, _cancellationToken);
				}
				catch (Exception e)
				{
					_logger.LogWarning(e, "Failed to add {File} to logs archive", file);
				}
			}
		}
	}

	private sealed class LogsCollectingLogger : ILogger
	{
		private readonly ILogger _innerLogger;
		private readonly TimeProvider _timeProvider;

		public StringBuilder LogsBuilder { get; } = new(1000);

		public LogsCollectingLogger(ILogger innerLogger, TimeProvider timeProvider)
		{
			_innerLogger = innerLogger;
			_timeProvider = timeProvider;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			var timestamp = _timeProvider.GetLocalNow();
			LogsBuilder.AppendLine(exception == null
				? $"[{logLevel}] {timestamp:yyyy.MM.dd HH:mm:ss.fff} {formatter(state, exception)}"
				: $"[{logLevel}] {timestamp:yyyy.MM.dd HH:mm:ss.fff} {formatter(state, exception)}{Environment.NewLine}{exception}");
			_innerLogger.Log(logLevel, eventId, state, exception, formatter);
		}

		public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _innerLogger.BeginScope(state);
	}
}