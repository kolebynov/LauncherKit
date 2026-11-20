using System.Text.Json;
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
		var mocker = new AutoMocker(MockBehavior.Strict);
		var handler = SetupHandler(mocker);
		handler.Setup(h => h.Handle(new OpenFileMessage("fail"))).Throws(new InvalidOperationException("Cannot open fail."));
		handler.Setup(h => h.Handle(new OpenFileMessage("b")));
		var sut = CreateSut(mocker, new CapturedLogs<LauncherMessageProcessor>());

		// Act
		sut.Process("unknown", Payload("a"));
		sut.Process(OpenFileMessage.Kind, "1");
		sut.Process(OpenFileMessage.Kind, Payload("fail"));
		sut.Process(OpenFileMessage.Kind, Payload("b"));

		// Assert
		handler.Verify(h => h.Handle(new OpenFileMessage("fail")), Times.Once);
		handler.Verify(h => h.Handle(new OpenFileMessage("b")), Times.Once);
		handler.VerifyNoOtherCalls();
	}

	[Test]
	public void Process_RegisteredKind_RunsHandlerOfProcessorScope()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var handler = SetupHandler(mocker);
		handler.Setup(h => h.Handle(new OpenFileMessage("a")));
		var sut = CreateSut(mocker, new CapturedLogs<LauncherMessageProcessor>());

		// Act
		sut.Process(OpenFileMessage.Kind, Payload("a"));

		// Assert
		mocker.GetMock<IServiceProvider>().Verify(s => s.GetService(typeof(ILauncherMessageHandler<OpenFileMessage>)), Times.Once);
		handler.Verify(h => h.Handle(new OpenFileMessage("a")), Times.Once);
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
	public void AddLauncherMessageHandler_RegisteredHandler_ProcessorDispatchesValidPayloadOnly()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var handler = mocker.GetMock<ILauncherMessageHandler<OpenFileMessage>>();
		handler.Setup(h => h.Handle(new OpenFileMessage("a")));
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddLauncherKitDesktop();
		services.AddScoped(_ => handler.Object);
		services.AddLauncherMessageHandler<OpenFileMessage, ILauncherMessageHandler<OpenFileMessage>>(
			TestMessagesJsonContext.Default.OpenFileMessage);
		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var sut = scope.ServiceProvider.GetRequiredService<ILauncherMessageProcessor>();

		// Act
		sut.Process(OpenFileMessage.Kind, Payload("a"));
		sut.Process(OpenFileMessage.Kind, "1");

		// Assert
		handler.Verify(h => h.Handle(new OpenFileMessage("a")), Times.Once);
		handler.VerifyNoOtherCalls();
	}

	[Test]
	public void Process_UnknownKind_LogsWarningWithoutWireKind()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		SetupHandler(mocker);
		var logs = new CapturedLogs<LauncherMessageProcessor>();
		var sut = CreateSut(mocker, logs);

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
		var mocker = new AutoMocker(MockBehavior.Strict);
		var handler = SetupHandler(mocker);
		var failure = new InvalidOperationException("Cannot open fail-path.");
		handler.Setup(h => h.Handle(new OpenFileMessage("fail-path"))).Throws(failure);
		var logs = new CapturedLogs<LauncherMessageProcessor>();
		var sut = CreateSut(mocker, logs);

		// Act
		sut.Process(OpenFileMessage.Kind, Payload("fail-path"));

		// Assert
		Assert.That(logs.Entries, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Error));
			Assert.That(logs.Entries[0].Message, Does.Contain(OpenFileMessage.Kind));
			Assert.That(logs.Entries[0].Exception, Is.SameAs(failure));
		});
	}

	/// <summary>Resolves the handler mock through the processor's service provider mock.</summary>
	private static Mock<ILauncherMessageHandler<OpenFileMessage>> SetupHandler(AutoMocker mocker)
	{
		var handler = mocker.GetMock<ILauncherMessageHandler<OpenFileMessage>>();
		mocker.GetMock<IServiceProvider>()
			.Setup(s => s.GetService(typeof(ILauncherMessageHandler<OpenFileMessage>)))
			.Returns(handler.Object);
		return handler;
	}

	/// <summary>Registers the <see cref="OpenFileMessage"/> kind as <c>AddLauncherMessageHandler</c> does.</summary>
	private static LauncherMessageProcessor CreateSut(AutoMocker mocker, CapturedLogs<LauncherMessageProcessor> logs)
	{
		mocker.Use<IEnumerable<LauncherMessageRegistration>>([
			new LauncherMessageRegistration(OpenFileMessage.Kind, (services, payload) => services
				.GetRequiredService<ILauncherMessageHandler<OpenFileMessage>>()
				.Handle(JsonSerializer.Deserialize(payload, TestMessagesJsonContext.Default.OpenFileMessage)
					?? throw new JsonException("The message is null."))),
		]);
		mocker.Use<ILogger<LauncherMessageProcessor>>(logs);
		return mocker.CreateInstance<LauncherMessageProcessor>();
	}

	private static string Payload(string path) =>
		LaunchRequest.Message(new OpenFileMessage(path), TestMessagesJsonContext.Default.OpenFileMessage).Payload;
}
