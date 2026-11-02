using System.Buffers;
using System.Globalization;
using NuGet.Versioning;

namespace LauncherKit.FeedEditor.Feed;

/// <summary>
/// Field rules mirroring what the launcher's JSON converters accept. Each returns an error message or null; the
/// same delegates drive inline field validation and <see cref="FeedValidator"/>.
/// </summary>
public static class FeedRules
{
	public const int Sha256HexLength = 64;

	private static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create("<>:\"/\\|?*");

	public static readonly Func<string?, string?> Required =
		value => string.IsNullOrWhiteSpace(value) ? "Обязательное поле." : null;

	public static readonly Func<string?, string?> Language = ValidateLanguage;

	private static string? ValidateLanguage(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "Обязательное поле.";
		try
		{
			return CultureInfo.GetCultureInfo(value).Name.Length == 0 ? "Укажите код языка." : null;
		}
		catch (CultureNotFoundException)
		{
			return "Некорректный код языка.";
		}
	}

	public static readonly Func<string?, string?> Version =
		value => string.IsNullOrWhiteSpace(value) ? "Обязательное поле."
			: NuGetVersion.TryParse(value, out _) ? null
			: "Некорректная версия (например, 3.1.0 или 3.2.0-beta.1).";

	public static readonly Func<string?, string?> VersionRange =
		value => string.IsNullOrWhiteSpace(value) ? "Обязательное поле."
			: NuGet.Versioning.VersionRange.TryParse(value, out _) ? null
			: "Некорректный диапазон версий (например, [3.1.0, 3.2.0)).";

	/// <summary>Optional download hash: empty, or a hex SHA-256 as accepted by the launcher's <c>Sha256Hash</c>.</summary>
	public static readonly Func<string?, string?> OptionalSha256 =
		value => string.IsNullOrEmpty(value) || IsSha256Hex(value) ? null : "Нужно 64 шестнадцатеричных символа (SHA-256).";

	public static readonly Func<string?, string?> Sha256 =
		value => IsSha256Hex(value) ? null : "Нужно 64 шестнадцатеричных символа (SHA-256).";

	/// <summary>A Discord snowflake ID, as the launcher reads it.</summary>
	public static readonly Func<string?, string?> DiscordId =
		value => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? null
			: "Нужен числовой ID Discord (ПКМ по роли → «Копировать ID роли»).";

	/// <summary>
	/// Mod Organizer separator name as shown, which the launcher turns into the directory <c>{name}_separator</c>, so it
	/// must be a valid Windows file name without that suffix.
	/// </summary>
	public static readonly Func<string?, string?> Separator =
		value => string.IsNullOrWhiteSpace(value) ? "Обязательное поле."
			: value.EndsWith("_separator", StringComparison.OrdinalIgnoreCase) ? "Укажите имя без суффикса _separator."
			: value.AsSpan().ContainsAny(InvalidFileNameChars) || value.Any(char.IsControl) ? "Имя не должно содержать символы < > : \" / \\ | ? *."
			: null;

	private static bool IsSha256Hex(string? value) => value is { Length: Sha256HexLength } && value.All(char.IsAsciiHexDigit);
}
