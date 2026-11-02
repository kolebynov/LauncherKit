using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

/// <summary>Discovers files for one collection. Sources may use the current installation path, not cached settings.</summary>
public interface ILogsSource
{
	IEnumerable<LogFile> GetFiles(string? modOrganizerDirectory, ILogger logger);
}

/// <summary>A source file and its relative archive entry name.</summary>
public readonly record struct LogFile(string FilePath, string EntryName);
