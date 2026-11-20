using System.Buffers.Binary;
using System.Text.Json;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// One-way frames from a relaunch to the owner until the relaunch closes the pipe: each a little-endian 32-bit length
/// and that many bytes of UTF-8 JSON <see cref="LaunchRequest"/>. Nothing is sent back.
/// </summary>
internal static class SingleInstanceProtocol
{
	public const int MaxRequestBytes = 64 * 1024;

	/// <summary>Bounds each connection on both ends, so a stalled peer never blocks the other.</summary>
	public static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(5);

	public union Frame(Frame.Request, Frame.Invalid, Frame.End)
	{
		public sealed record Request(LaunchRequest Value);

		/// <summary>A well-framed but malformed or incompatible request; the following frames are still readable.</summary>
		public sealed record Invalid;

		/// <summary>The relaunch closed the pipe.</summary>
		public sealed record End;
	}

	/// <summary>The request's JSON; throws <see cref="ArgumentException"/> when it exceeds <see cref="MaxRequestBytes"/>.</summary>
	public static byte[] Serialize(LaunchRequest request)
	{
		var json = JsonSerializer.SerializeToUtf8Bytes(request, SingleInstanceJsonContext.Default.LaunchRequest);
		return json.Length <= MaxRequestBytes
			? json
			: throw new ArgumentException($"The launch request exceeds {MaxRequestBytes} bytes.", nameof(request));
	}

	public static async Task WriteFrame(Stream stream, byte[] json, CancellationToken cancellationToken)
	{
		var length = new byte[sizeof(int)];
		BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
		await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
		await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Reads the next frame; throws <see cref="InvalidDataException"/> for an invalid length, after which the stream
	/// cannot be resynchronized, and <see cref="EndOfStreamException"/> for a truncated frame.
	/// </summary>
	public static async Task<Frame> ReadFrame(Stream stream, CancellationToken cancellationToken)
	{
		var lengthBytes = new byte[sizeof(int)];
		var read = await stream.ReadAtLeastAsync(lengthBytes, lengthBytes.Length, throwOnEndOfStream: false, cancellationToken)
			.ConfigureAwait(false);
		if (read == 0) return Frame.End.Instance;
		if (read < lengthBytes.Length) throw new EndOfStreamException();
		var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
		if (length is <= 0 or > MaxRequestBytes) throw new InvalidDataException("The launch request frame length is invalid.");
		var json = new byte[length];
		await stream.ReadExactlyAsync(json, cancellationToken).ConfigureAwait(false);
		try
		{
			var request = JsonSerializer.Deserialize(json, SingleInstanceJsonContext.Default.LaunchRequest);
			return request is { Version: LaunchRequest.CurrentVersion, Kind: not null }
				&& request.Payload.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
				? new Frame.Request(request)
				: Frame.Invalid.Instance;
		}
		catch (JsonException)
		{
			return Frame.Invalid.Instance;
		}
	}
}
