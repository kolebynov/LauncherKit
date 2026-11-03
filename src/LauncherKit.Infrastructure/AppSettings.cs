namespace LauncherKit.Infrastructure;

public sealed class AppSettings
{
	public string? AppDataDirectory { get; set; }

	public string? SkyrimDirectory { get; set; }

	/// <summary>
	/// The UI language, <c>en</c> or <c>ru</c>. Any other value, including <see langword="null"/> and empty, means it
	/// was never chosen: the first setup then asks for it.
	/// </summary>
	public string? Language { get; set; }

	public string? GetDownloadsDirectory() => CombinePaths(AppDataDirectory, "Downloads");

	public string? GetTempDataDirectory() => CombinePaths(AppDataDirectory, "Temp");

	private static string? CombinePaths(string? first, string second) =>
		string.IsNullOrEmpty(first) ? null : Path.Combine(first, second);
}
