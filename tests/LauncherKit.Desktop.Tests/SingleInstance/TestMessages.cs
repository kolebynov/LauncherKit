using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using LauncherKit.Desktop.SingleInstance;

namespace LauncherKit.Desktop.Tests.SingleInstance;

internal sealed record OpenFileMessage(string Path);

[JsonSerializable(typeof(OpenFileMessage))]
[JsonSerializable(typeof(int))]
internal sealed partial class TestMessagesJsonContext : JsonSerializerContext;

/// <summary>A scoped service whose identity tells which scope handled a message.</summary>
internal sealed class ScopeMarker;

internal sealed class HandledMessages
{
	public List<(string Path, ScopeMarker Scope)> Items { get; } = [];
}

/// <summary>Records each message with its scope; a path starting with <c>fail</c> throws with the path in its message.</summary>
internal sealed class OpenFileHandler(HandledMessages handled, ScopeMarker scope) : ILauncherMessageHandler<OpenFileMessage>
{
	public const string Kind = "openFile";

	public void Handle(OpenFileMessage message)
	{
		if (message.Path.StartsWith("fail", StringComparison.Ordinal))
			throw new InvalidOperationException($"Cannot open {message.Path}.");
		handled.Items.Add((message.Path, scope));
	}
}

/// <summary>Collects every formatted log entry with its exception text.</summary>
internal sealed class CapturedLogs : ILoggerProvider, ILogger
{
	public List<string> Entries { get; } = [];

	public ILogger CreateLogger(string categoryName) => this;

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter) =>
		Entries.Add($"{formatter(state, exception)} {exception}");

	public void Dispose()
	{
	}
}
