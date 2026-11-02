using LauncherKit.Core.Utils;

namespace LauncherKit.Core.Tests.Utils;

[TestFixture]
public sealed class PathUtilsTests
{
	private static string Root => OperatingSystem.IsWindows() ? @"C:\" : "/";

	[Test]
	public void AreSameOrNested_SamePathWithDifferentSeparatorsAndTrailingSeparator_ReturnsTrue()
	{
		// Arrange
		var first = Path.Combine(Root, "games", "skyrim");
		var second = Path.Combine(Root, "games", "skyrim") + Path.DirectorySeparatorChar;

		// Act
		var result = PathUtils.AreSameOrNested(first, second);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void AreSameOrNested_SecondIsChildOfFirst_ReturnsTrue()
	{
		// Arrange
		var parent = Path.Combine(Root, "games", "skyrim");
		var child = Path.Combine(parent, "data", "launcher");

		// Act
		var result = PathUtils.AreSameOrNested(parent, child);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void AreSameOrNested_FirstIsChildOfSecond_ReturnsTrue()
	{
		// Arrange
		var parent = Path.Combine(Root, "launcher-data");
		var child = Path.Combine(parent, "skyrim");

		// Act
		var result = PathUtils.AreSameOrNested(child, parent);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void AreSameOrNested_ChildReachedThroughRelativeSegments_ReturnsTrue()
	{
		// Arrange
		var parent = Path.Combine(Root, "games", "skyrim");
		var child = Path.Combine(Root, "games", "other", "..", "skyrim", "data");

		// Act
		var result = PathUtils.AreSameOrNested(parent, child);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void AreSameOrNested_DriveRootAndNestedDirectory_ReturnsTrue()
	{
		// Arrange
		var nested = Path.Combine(Root, "games");

		// Act
		var result = PathUtils.AreSameOrNested(Root, nested);

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public void AreSameOrNested_SiblingDirectoriesWithCommonNamePrefix_ReturnsFalse()
	{
		// Arrange
		var first = Path.Combine(Root, "games", "skyrim");
		var second = Path.Combine(Root, "games", "skyrim-data");

		// Act
		var result = PathUtils.AreSameOrNested(first, second);

		// Assert
		Assert.That(result, Is.False);
	}

	[Test]
	public void AreSameOrNested_UnrelatedDirectories_ReturnsFalse()
	{
		// Arrange
		var first = Path.Combine(Root, "games", "skyrim");
		var second = Path.Combine(Root, "launcher", "data");

		// Act
		var result = PathUtils.AreSameOrNested(first, second);

		// Assert
		Assert.That(result, Is.False);
	}

	[Test]
	public void AreSameOrNested_EmptyPath_Throws()
	{
		// Arrange
		var directory = Path.Combine(Root, "games");

		// Act & Assert
		Assert.That(() => PathUtils.AreSameOrNested(directory, string.Empty), Throws.ArgumentException);
	}
}
