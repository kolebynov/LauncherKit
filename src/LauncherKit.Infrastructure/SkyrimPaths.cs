namespace LauncherKit.Infrastructure;

/// <summary>Installation-local paths, separate from the launcher's generic download and temporary storage.</summary>
public static class SkyrimPaths
{
	public static string? GetSkyrimAppDataDirectory(this AppSettings settings, LauncherProductOptions product)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		return Combine(settings.SkyrimDirectory, product.SkyrimDataDirectoryName);
	}

	public static string? GetModOrganizerDirectory(this AppSettings settings, LauncherProductOptions product)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		return Combine(settings.SkyrimDirectory, product.ModOrganizerDirectoryName);
	}

	private static string? Combine(string? directory, string relativePath) =>
		string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, relativePath);
}
