namespace LauncherKit.Core.Utils;

public static class PathUtils
{
	private static readonly StringComparison PathComparison =
		OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	/// <summary>
	/// Checks whether two directory paths point to the same directory or one of them is nested into the other one.
	/// </summary>
	public static bool AreSameOrNested(string firstDirectory, string secondDirectory)
	{
		ArgumentException.ThrowIfNullOrEmpty(firstDirectory);
		ArgumentException.ThrowIfNullOrEmpty(secondDirectory);

		var first = Normalize(firstDirectory);
		var second = Normalize(secondDirectory);
		return first.Length <= second.Length
			? IsSameOrDescendant(second, first)
			: IsSameOrDescendant(first, second);
	}

	private static bool IsSameOrDescendant(string longerPath, string shorterPath)
	{
		if (!longerPath.StartsWith(shorterPath, PathComparison))
		{
			return false;
		}

		// A root path such as "C:\" keeps its trailing separator after normalization.
		return longerPath.Length == shorterPath.Length ||
		       shorterPath.EndsWith(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
		       longerPath[shorterPath.Length] == Path.DirectorySeparatorChar;
	}

	private static string Normalize(string directory) =>
		Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
}
