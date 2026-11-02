namespace LauncherKit.Infrastructure;

public sealed class AppSettings
{
	public string? AppDataDirectory { get; set; }

	public string? SkyrimDirectory { get; set; }

	public string Language { get; set; } = string.Empty;

	public string? GetDownloadsDirectory() => CombinePaths(AppDataDirectory, "Downloads");

	public string? GetTempDataDirectory() => CombinePaths(AppDataDirectory, "Temp");

	private static string? CombinePaths(string? first, string second) =>
		string.IsNullOrEmpty(first) ? null : Path.Combine(first, second);
}
