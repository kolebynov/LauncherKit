using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using LauncherKit.Desktop.SingleInstance;

namespace LauncherKit.Desktop.Tests.SingleInstance;

internal sealed record OpenFileMessage(string Path) : ILauncherMessage
{
	public static string Kind => "openFile";

	public static string? PathOf(string? payload) =>
		payload is null ? null : JsonSerializer.Deserialize(payload, TestMessagesJsonContext.Default.OpenFileMessage)?.Path;
}

internal sealed record BlankKindMessage : ILauncherMessage
{
	public static string Kind => " ";
}

internal sealed class BlankKindHandler : ILauncherMessageHandler<BlankKindMessage>
{
	public void Handle(BlankKindMessage message) => throw new AssertionException("Unexpected message.");
}

[JsonSerializable(typeof(OpenFileMessage))]
[JsonSerializable(typeof(BlankKindMessage))]
internal sealed partial class TestMessagesJsonContext : JsonSerializerContext;

internal sealed class OpenFileHandler : ILauncherMessageHandler<OpenFileMessage>
{
	public void Handle(OpenFileMessage message) => throw new AssertionException("Unexpected message.");
}

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
