using System.Numerics;

namespace LauncherKit.Core.Utils;

/// <summary>
/// Converts byte counts to and from binary units (1 KB = 1024 bytes) and formats them the way the whole launcher shows
/// sizes and speeds.
/// </summary>
public static class BytesFormatter
{
	public const int KiloByte = 1024;
	public const int MegaByte = 1024 * KiloByte;
	public const int GigaByte = 1024 * MegaByte;

	/// <summary>Converts <paramref name="bytes"/> to the unit of <paramref name="unitSize"/> bytes.</summary>
	public static T ToUnit<T>(T bytes, int unitSize)
		where T : INumber<T> =>
		bytes / T.CreateChecked(unitSize);

	/// <summary>Converts <paramref name="value"/> in the unit of <paramref name="unitSize"/> bytes to bytes.</summary>
	public static T FromUnit<T>(T value, int unitSize)
		where T : INumber<T> =>
		value * T.CreateChecked(unitSize);

	/// <summary>
	/// Formats <paramref name="bytes"/> in the largest unit it reaches with two decimals in the current culture,
	/// e.g. "1.50 GB", or "1.50 GB/s" for a <see cref="Measurement.Speed"/>.
	/// </summary>
	public static string Format(double bytes, Measurement measurement)
	{
		var (value, unit) = bytes switch
		{
			< KiloByte => (bytes, "B"),
			< MegaByte => (bytes / KiloByte, "KB"),
			< GigaByte => (bytes / MegaByte, "MB"),
			_ => (bytes / GigaByte, "GB"),
		};
		var formatted = $"{value:F2} {unit}";

		return measurement switch
		{
			Measurement.Size => formatted,
			Measurement.Speed => $"{formatted}/s",
		};
	}

	public union Measurement(Measurement.Size, Measurement.Speed)
	{
		public sealed record Size;

		public sealed record Speed;
	}
}
