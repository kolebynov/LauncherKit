using LauncherKit.Desktop.SingleInstance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class LauncherMessageProcessorTests
{
	[Test]
	public void Process_UnknownKindInvalidPayloadOrFailingHandler_KeepsHandlingLaterMessages()
	{
		// Arrange
		using var provider = CreateServices();
		using var scope = provider.CreateScope();
		var sut = scope.ServiceProvider.GetRequiredService<LauncherMessageProcessor>();

		// Act
		sut.Process("unknown", Payload("a"));
		sut.Process(OpenFileMessage.Kind, "1");
		sut.Process(OpenFileMessage.Kind, Payload("fail"));
		sut.Process(OpenFileMessage.Kind, Payload("b"));

		// Assert
		Assert.That(provider.GetRequiredService<HandledMessages>().Items.Select(i => i.Path), Is.EqualTo(new[] { "b" }));
	}

	[Test]
	public void Process_RegisteredKind_RunsHandlerOfProcessorScope()
	{
		// Arrange
		using var provider = CreateServices();
		using var scope = provider.CreateScope();
		var sut = scope.ServiceProvider.GetRequiredService<LauncherMessageProcessor>();

		// Act
		sut.Process(OpenFileMessage.Kind, Payload("a"));

		// Assert
		Assert.That(provider.GetRequiredService<HandledMessages>().Items.Single().Scope,
			Is.SameAs(scope.ServiceProvider.GetRequiredService<ScopeMarker>()));
	}

	[Test]
	public void AddLauncherMessageHandler_KindAlreadyRegistered_Throws()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddLauncherMessageHandler<OpenFileMessage, OpenFileHandler>(TestMessagesJsonContext.Default.OpenFileMessage);

		// Act & Assert
		Assert.That(() => services.AddLauncherMessageHandler<OpenFileMessage, OpenFileHandler>(
			TestMessagesJsonContext.Default.OpenFileMessage), Throws.ArgumentException);
	}

	[Test]
	public void AddLauncherMessageHandler_BlankKind_Throws()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act & Assert
		Assert.That(() => services.AddLauncherMessageHandler<BlankKindMessage, BlankKindHandler>(
			TestMessagesJsonContext.Default.BlankKindMessage), Throws.ArgumentException);
	}

	[Test]
	public void Process_UnknownKind_LogsWarningWithoutWireKind()
	{
		// Arrange
		var logs = new CapturedLogs();
		using var provider = CreateServices(logs);
		using var scope = provider.CreateScope();
		var sut = scope.ServiceProvider.GetRequiredService<LauncherMessageProcessor>();

		// Act
		sut.Process("secret-kind", Payload("a"));

		// Assert
		Assert.That(logs.Entries, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
			Assert.That(logs.Entries[0].Message, Does.Not.Contain("secret"));
			Assert.That(logs.Entries[0].Exception, Is.Null);
		});
	}

	[Test]
	public void Process_FailingHandler_LogsErrorWithExceptionAndRegisteredKind()
	{
		// Arrange
		var logs = new CapturedLogs();
		using var provider = CreateServices(logs);
		using var scope = provider.CreateScope();
		var sut = scope.ServiceProvider.GetRequiredService<LauncherMessageProcessor>();

		// Act
		sut.Process(OpenFileMessage.Kind, Payload("fail-path"));

		// Assert
		Assert.That(logs.Entries, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Error));
			Assert.That(logs.Entries[0].Message, Does.Contain(OpenFileMessage.Kind));
			Assert.That(logs.Entries[0].Exception, Is.TypeOf<InvalidOperationException>()
				.With.Message.Contain("fail-path"));
		});
	}

	internal static ServiceProvider CreateServices(ILoggerProvider? logs = null)
	{
		var services = new ServiceCollection();
		services.AddLauncherKitDesktop();
		services.AddLogging(builder =>
		{
			builder.ClearProviders();
			if (logs is not null) builder.AddProvider(logs);
		});
		services.AddSingleton<HandledMessages>();
		services.AddScoped<ScopeMarker>();
		services.AddLauncherMessageHandler<OpenFileMessage, OpenFileHandler>(TestMessagesJsonContext.Default.OpenFileMessage);
		return services.BuildServiceProvider();
	}

	internal static string Payload(string path) =>
		LaunchRequest.Message(new OpenFileMessage(path), TestMessagesJsonContext.Default.OpenFileMessage).Payload;
}
