using Microsoft.Extensions.Logging;

namespace LauncherKit.Infrastructure;

public sealed class SimpleConsoleLogger : ILogger
{
	private readonly TimeProvider _timeProvider;
	private readonly LogLevel _minimumLevel;
	private readonly TextWriter? _writer;

	public SimpleConsoleLogger(
		TimeProvider timeProvider,
		LogLevel minimumLevel = LogLevel.Trace,
		TextWriter? writer = null)
	{
		_timeProvider = timeProvider;
		_minimumLevel = minimumLevel;
		_writer = writer;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel))
		{
			return;
		}

		var now = _timeProvider.GetLocalNow();
		var writer = _writer ?? Console.Out;
		writer.WriteLine(exception == null
			? $"[{logLevel}] {now:yyyy.MM.dd HH:mm:ss.fff} {formatter(state, exception)}"
			: $"[{logLevel}] {now:yyyy.MM.dd HH:mm:ss.fff} {formatter(state, exception)}{Environment.NewLine}{exception}");
	}

	public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel && logLevel != LogLevel.None;

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
		throw new NotSupportedException("Scope is not supported");
}
