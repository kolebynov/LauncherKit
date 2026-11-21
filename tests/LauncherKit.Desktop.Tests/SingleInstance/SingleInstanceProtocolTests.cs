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
		string[] args = ["a.7z", "", @"..\Моды\a ""b"".7z", "--flag"];
		var first = new LaunchContext(args, @"C:\Мой каталог", false);
		var empty = new LaunchContext([], @"D:\other", false);
		await SingleInstanceProtocol.WriteFrame(stream, SingleInstanceProtocol.Serialize(first), CancellationToken.None);
		await SingleInstanceProtocol.WriteFrame(stream, SingleInstanceProtocol.Serialize(empty), CancellationToken.None);
		stream.Position = 0;

		// Act
		var firstFrame = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);
		var emptyFrame = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);
		var end = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			var a = firstFrame is SingleInstanceProtocol.Frame.Request(var f) ? f : null;
			Assert.That(a?.Args, Is.EqualTo(args));
			Assert.That(a?.WorkingDirectory, Is.EqualTo(@"C:\Мой каталог"));
			Assert.That(a?.IsFirstLaunch, Is.False);
			var b = emptyFrame is SingleInstanceProtocol.Frame.Request(var e) ? e : null;
			Assert.That(b?.Args, Is.Empty);
			Assert.That(b?.WorkingDirectory, Is.EqualTo(@"D:\other"));
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

	[Test]
	public async Task ReadFrame_TruncatedBody_ThrowsEndOfStream()
	{
		// Arrange
		using var stream = new MemoryStream(Frame(10, "{}"u8.ToArray()));

		// Act & Assert
		await Assert.ThatAsync(() => SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None),
			Throws.InstanceOf<EndOfStreamException>());
	}

	[TestCase("not json")]
	[TestCase("""{"Kind":"openFile","Payload":"{}"}""")]
	[TestCase("""{"Args":["a"],"WorkingDirectory":"C:\\w","IsFirstLaunch":true}""")]
	[TestCase("""{"Args":null,"WorkingDirectory":"C:\\w","IsFirstLaunch":false}""")]
	[TestCase("""{"Args":["a",null],"WorkingDirectory":"C:\\w","IsFirstLaunch":false}""")]
	[TestCase("""{"Args":["a"],"WorkingDirectory":null,"IsFirstLaunch":false}""")]
	[TestCase("""{"WorkingDirectory":"C:\\w","IsFirstLaunch":false}""")]
	[TestCase("""{"Args":["a"],"IsFirstLaunch":false}""")]
	[TestCase("""{"Args":["a"],"WorkingDirectory":"C:\\w"}""")]
	[TestCase("""{"Args":"a","WorkingDirectory":"C:\\w","IsFirstLaunch":false}""")]
	public async Task ReadFrame_MalformedOrForgedJson_ReturnsInvalid(string json)
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
	public async Task ReadFrame_ValidFrameAfterInvalidJson_ReturnsRequest()
	{
		// Arrange
		using var stream = new MemoryStream();
		var invalid = "not json"u8.ToArray();
		await stream.WriteAsync(Frame(invalid.Length, invalid));
		await SingleInstanceProtocol.WriteFrame(stream, SingleInstanceProtocol.Serialize(new LaunchContext(["b"], @"C:\w", false)),
			CancellationToken.None);
		stream.Position = 0;

		// Act
		var first = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);
		var second = await SingleInstanceProtocol.ReadFrame(stream, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(first.Value, Is.TypeOf<SingleInstanceProtocol.Frame.Invalid>());
			Assert.That(second is SingleInstanceProtocol.Frame.Request(var r) ? r.Args : null, Is.EqualTo(new[] { "b" }));
		});
	}

	[Test]
	public void Serialize_AtLimit_ReturnsMaxBytes()
	{
		// Arrange
		var overhead = SingleInstanceProtocol.Serialize(Context("")).Length;
		var context = Context(new string('a', SingleInstanceProtocol.MaxRequestBytes - overhead));

		// Act
		var json = SingleInstanceProtocol.Serialize(context);

		// Assert
		Assert.That(json, Has.Length.EqualTo(SingleInstanceProtocol.MaxRequestBytes));
	}

	[Test]
	public void Serialize_OneByteOverLimit_Throws()
	{
		// Arrange
		var overhead = SingleInstanceProtocol.Serialize(Context("")).Length;
		var context = Context(new string('a', SingleInstanceProtocol.MaxRequestBytes - overhead + 1));

		// Act & Assert
		Assert.That(() => SingleInstanceProtocol.Serialize(context), Throws.ArgumentException);
	}

	private static LaunchContext Context(string arg) => new([arg], @"C:\w", false);

	private static byte[] Frame(int length, byte[] body)
	{
		var frame = new byte[sizeof(int) + body.Length];
		BinaryPrimitives.WriteInt32LittleEndian(frame, length);
		body.CopyTo(frame, sizeof(int));
		return frame;
	}
}
