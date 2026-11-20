using System.Buffers.Binary;
using System.Text;
using LauncherKit.Desktop.SingleInstance;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class SingleInstanceProtocolTests
{
	[Test]
	public async Task ReadFrame_WrittenFrames_RoundTripInOrderThenEnd()
	{
		// Arrange
		using var stream = new MemoryStream();
		var message = LaunchRequest.Message(OpenFileHandler.Kind, new OpenFileMessage(@"C:\Моды\a ""b"".7z"),
			TestMessagesJsonContext.Default.OpenFileMessage);
		var bringToFront = LaunchRequest.Message(BringToFrontMessage.Kind, BringToFrontMessage.Instance,
			SingleInstanceJsonContext.Default.BringToFrontMessage);
		await SingleInstanceProtocol.WriteFrame(stream, SingleInstanceProtocol.Serialize(bringToFront), CancellationToken.None);
		await SingleInstanceProtocol.WriteFrame(stream, SingleInstanceProtocol.Serialize(message), CancellationToken.None);
		stream.Position = 0;

		// Act
		var first = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);
		var received = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);
		var end = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(first is SingleInstanceProtocol.Frame.Request(var f) ? f.Kind : null, Is.EqualTo(BringToFrontMessage.Kind));
			var request = received is SingleInstanceProtocol.Frame.Request(var r) ? r : null;
			Assert.That(request?.Kind, Is.EqualTo(OpenFileHandler.Kind));
			Assert.That(request?.Payload.GetProperty("Path").GetString(), Is.EqualTo(@"C:\Моды\a ""b"".7z"));
			Assert.That(end.Value, Is.TypeOf<SingleInstanceProtocol.Frame.End>());
		});
	}

	[TestCase(0)]
	[TestCase(-1)]
	[TestCase(SingleInstanceProtocol.MaxRequestBytes + 1)]
	public async Task ReadFrame_InvalidLength_ThrowsInvalidData(int length)
	{
		// Arrange
		using var stream = new MemoryStream(Frame(length, []));

		// Act & Assert
		await Assert.ThatAsync(() => SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None),
			Throws.InstanceOf<InvalidDataException>());
	}

	[Test]
	public async Task ReadFrame_TruncatedLength_ThrowsEndOfStream()
	{
		// Arrange
		using var stream = new MemoryStream([1, 0]);

		// Act & Assert
		await Assert.ThatAsync(() => SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None),
			Throws.InstanceOf<EndOfStreamException>());
	}

	[TestCase("not json")]
	[TestCase("""{"Version":2,"Kind":"openFile","Payload":{}}""")]
	[TestCase("""{"Version":1,"Kind":"openFile","Payload":null}""")]
	[TestCase("""{"Version":1,"Kind":null,"Payload":{}}""")]
	[TestCase("""{"Version":1,"Payload":{}}""")]
	public async Task ReadFrame_MalformedOrIncompatibleJson_ReturnsInvalid(string json)
	{
		// Arrange
		var bytes = Encoding.UTF8.GetBytes(json);
		using var stream = new MemoryStream(Frame(bytes.Length, bytes));

		// Act
		var frame = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);

		// Assert
		Assert.That(frame.Value, Is.TypeOf<SingleInstanceProtocol.Frame.Invalid>());
	}

	[Test]
	public void Serialize_Oversized_Throws()
	{
		// Arrange
		var message = LaunchRequest.Message(OpenFileHandler.Kind,
			new OpenFileMessage(new string('a', SingleInstanceProtocol.MaxRequestBytes)), TestMessagesJsonContext.Default.OpenFileMessage);

		// Act & Assert
		Assert.That(() => SingleInstanceProtocol.Serialize(message), Throws.ArgumentException);
	}

	private static byte[] Frame(int length, byte[] body)
	{
		var frame = new byte[sizeof(int) + body.Length];
		BinaryPrimitives.WriteInt32LittleEndian(frame, length);
		body.CopyTo(frame, sizeof(int));
		return frame;
	}
}
