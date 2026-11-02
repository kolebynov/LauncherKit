using LauncherKit.Core.Models;

namespace LauncherKit.Core.Tests.Models;

[TestFixture]
public sealed class SkyrimRelativePathTests
{
	[Test]
	public void Constructor_RelativePath_KeepsPathAsIs()
	{
		// Act
		var path = new SkyrimRelativePath(Path.Combine("Data", "Skse", "plugin.dll"));

		// Assert
		Assert.That(path.Path, Is.EqualTo(Path.Combine("Data", "Skse", "plugin.dll")));
	}

	[Test]
	public void Constructor_EmptyPath_IsAllowedAndRepresentsRoot()
	{
		// Act
		var path = new SkyrimRelativePath(string.Empty);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(path.Path, Is.Empty);
			Assert.That(SkyrimRelativePath.Root.Path, Is.Empty);
		});
	}

	[Test]
	public void Constructor_RootedPath_ThrowsArgumentException()
	{
		// Act
		// Assert
		Assert.That(() => new SkyrimRelativePath(@"C:\Games\Skyrim"), Throws.ArgumentException);
	}

	[Test]
	public void Constructor_PathWithParentTraversal_ThrowsArgumentException()
	{
		// Act
		// Assert
		Assert.That(() => new SkyrimRelativePath(Path.Combine("Data", "..", "..", "Windows")), Throws.ArgumentException);
	}

	[Test]
	public void Constructor_NullPath_ThrowsArgumentNullException()
	{
		// Act
		// Assert
		Assert.That(() => new SkyrimRelativePath(null!), Throws.ArgumentNullException);
	}

	[Test]
	public void Append_RelativeSegment_CombinesWithExistingPath()
	{
		// Arrange
		var path = new SkyrimRelativePath("Data");

		// Act
		var appended = path.Append("Textures");

		// Assert
		Assert.That(appended.Path, Is.EqualTo(Path.Combine("Data", "Textures")));
	}

	[Test]
	public void Append_TraversingSegment_ThrowsArgumentException()
	{
		// Arrange
		var path = new SkyrimRelativePath("Data");

		// Act
		// Assert
		Assert.That(() => path.Append(".."), Throws.ArgumentException);
	}

	[Test]
	public void ToAbsolute_RelativePath_ResolvesAgainstSkyrimDirectory()
	{
		// Arrange
		var path = new SkyrimRelativePath(Path.Combine("Data", "Skyrim.esm"));

		// Act
		var absolute = path.ToAbsolute(@"C:\Games\Skyrim");

		// Assert
		Assert.That(absolute, Is.EqualTo(@"C:\Games\Skyrim\Data\Skyrim.esm"));
	}

	[Test]
	public void ToAbsolute_RootPath_ReturnsSkyrimDirectory()
	{
		// Act
		var absolute = SkyrimRelativePath.Root.ToAbsolute(@"C:\Games\Skyrim");

		// Assert
		Assert.That(absolute, Is.EqualTo(@"C:\Games\Skyrim"));
	}
}
