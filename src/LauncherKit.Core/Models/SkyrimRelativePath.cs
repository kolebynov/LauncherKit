namespace LauncherKit.Core.Models;

public readonly struct SkyrimRelativePath
{
	public string Path { get; }

	public static SkyrimRelativePath Root { get; } = new(string.Empty);

	public SkyrimRelativePath(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		if (path.Length != 0)
		{
			if (System.IO.Path.IsPathRooted(path))
			{
				throw new ArgumentException("Path must not be rooted", nameof(path));
			}

			if (path.Contains("..", StringComparison.Ordinal))
			{
				throw new ArgumentException("Path must not contain '..'", nameof(path));
			}
		}

		Path = path;
	}

	public SkyrimRelativePath Append(string path) => new(System.IO.Path.Combine(Path, path));

	public string ToAbsolute(string skyrimDirectory) => System.IO.Path.GetFullPath(Path, skyrimDirectory);
}