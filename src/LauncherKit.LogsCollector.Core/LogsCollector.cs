using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using LauncherKit.Infrastructure;

namespace LauncherKit.LogsCollector.Core;

public sealed class LogsCollector : ILogsCollector
{
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly IEnumerable<ILogsSource> _sources;
	private readonly ILogger<LogsCollector> _logger;
	private readonly TimeProvider _timeProvider;

	public LogsCollector(IFileSystemUtils fileSystemUtils, IEnumerable<ILogsSource> sources,
		ILogger<LogsCollector> logger, TimeProvider timeProvider)
	{
		_fileSystemUtils = fileSystemUtils;
		_sources = sources;
		_logger = logger;
		_timeProvider = timeProvider;
	}

	public Task<Exception?> CollectLogs(string outputFileDirectory, string? modOrganizerDirectory, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(outputFileDirectory);

		return new Collector(_fileSystemUtils, _sources, _logger, _timeProvider, outputFileDirectory,
			modOrganizerDirectory, cancellationToken).Collect();
	}

	private sealed class Collector
	{
		private readonly IFileSystemUtils _fileSystemUtils;
		private readonly IEnumerable<ILogsSource> _sources;
		private readonly LogsCollectingLogger _logger;
		private readonly string _outputFileDirectory;
		private readonly string? _modOrganizerDirectory;
		private readonly CancellationToken _cancellationToken;

		public Collector(IFileSystemUtils fileSystemUtils, IEnumerable<ILogsSource> sources,
			ILogger logger, TimeProvider timeProvider, string outputFileDirectory, string? modOrganizerDirectory,
			CancellationToken cancellationToken)
		{
			_fileSystemUtils = fileSystemUtils;
			_sources = sources;
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
				_logger.LogInformation("Start collecting logs to {LogsArchivePath}", logsArchivePath);

				_fileSystemUtils.EnsureDirectoryExists(_outputFileDirectory);
				logsZipArchive = new ZipArchive(File.Create(logsArchivePath), ZipArchiveMode.Create);

				foreach (var source in _sources)
				{
					await AddLogEntries(logsZipArchive, source.GetFiles(_modOrganizerDirectory, _logger));
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

		private async Task AddLogEntries(ZipArchive logsZipArchive, IEnumerable<LogFile> files)
		{
			foreach (var logFile in files)
			{
				var file = logFile.FilePath;
				_logger.LogInformation("Adding {File} to logs archive", file);
				if (!File.Exists(file))
				{
					_logger.LogWarning("File {File} does not exist", file);
					continue;
				}

				try
				{
					using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
					var entry = logsZipArchive.CreateEntry(logFile.EntryName);
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