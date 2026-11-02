namespace LauncherKit.TestSupport;

/// <summary>
/// Per-test unique directory under <see cref="Path.GetTempPath"/>; never inside the repository tree.
/// </summary>
public sealed class TempDirectory : IDisposable
{
	public TempDirectory()
	{
		Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stb-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path);
	}

	public string Path { get; }

	public string CreateFile(string relativePath, string content = "")
	{
		var fullPath = Combine(relativePath);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
		File.WriteAllText(fullPath, content);
		return fullPath;
	}

	public string CreateFile(string relativePath, byte[] content)
	{
		var fullPath = Combine(relativePath);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
		File.WriteAllBytes(fullPath, content);
		return fullPath;
	}

	public string CreateDirectory(string relativePath)
	{
		var fullPath = Combine(relativePath);
		Directory.CreateDirectory(fullPath);
		return fullPath;
	}

	public string Combine(string relativePath) => System.IO.Path.Combine(Path, relativePath);

	/// <summary>
	/// Deletion failures are swallowed: a leaked handle must not fail an otherwise passing test.
	/// </summary>
	public void Dispose()
	{
		try
		{
			Directory.Delete(Path, true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
