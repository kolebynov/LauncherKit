using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Tests.SingleInstance;

/// <summary>Collects every log entry with its level, formatted message and exception.</summary>
internal sealed class CapturedLogs<TCategory> : ILogger<TCategory>
{
	public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter) =>
		Entries.Add((logLevel, formatter(state, exception), exception));
}
