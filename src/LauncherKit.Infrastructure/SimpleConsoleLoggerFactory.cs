using Microsoft.Extensions.Logging;

namespace LauncherKit.Infrastructure;

public sealed class SimpleConsoleLoggerFactory : ILoggerFactory
{
	private readonly SimpleConsoleLogger _logger;

	public SimpleConsoleLoggerFactory(TimeProvider timeProvider)
	{
		_logger = new SimpleConsoleLogger(timeProvider);
	}

	public ILogger CreateLogger(string categoryName) => _logger;

	public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("Not supported");

	public void Dispose()
	{
	}
}
