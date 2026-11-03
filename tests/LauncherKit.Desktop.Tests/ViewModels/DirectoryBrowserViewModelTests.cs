using LauncherKit.Desktop.ViewModels;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class DirectoryBrowserViewModelTests
{

	[Test]
	public void SelectedDirectory_RelativePath_HasValidationError()
	{
		// Arrange
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DirectoryBrowserViewModel>();

		// Act
		sut.SelectedDirectory = "relative";

		// Assert
		Assert.That(sut.HasErrors, Is.True);
	}

	[Test]
	public void SelectedDirectory_ErrorCorrected_ClearsErrorsAndRaisesEvents()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DirectoryBrowserViewModel>();
		sut.SelectedDirectory = "relative";
		var errorsChanged = new List<string?>();
		var propertiesChanged = new List<string?>();
		sut.ErrorsChanged += (_, e) => errorsChanged.Add(e.PropertyName);
		sut.PropertyChanged += (_, e) => propertiesChanged.Add(e.PropertyName);

		// Act
		sut.SelectedDirectory = temp.Path;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.HasErrors, Is.False);
			Assert.That(sut.GetErrors(null), Is.Empty);
			Assert.That(errorsChanged, Is.EqualTo(new[] { nameof(DirectoryBrowserViewModel.SelectedDirectory) }));
			Assert.That(propertiesChanged, Does.Contain(nameof(DirectoryBrowserViewModel.HasErrors)));
		});
	}

	[Test]
	public void ConflictingDirectory_SelectedDirectoryNotSet_ReportsInvalidPath()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DirectoryBrowserViewModel>();

		// Act
		sut.ConflictingDirectory = temp.Path;

		// Assert
		Assert.That(sut.GetErrors(null), Is.EqualTo(new[] { DirectoryValidationError.InvalidPath }));
	}

	[Test]
	public void SelectedDirectory_FullyQualifiedMissingPath_IsValid()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DirectoryBrowserViewModel>();

		// Act
		sut.SelectedDirectory = temp.Combine("Skyrim");

		// Assert
		Assert.That(sut.HasErrors, Is.False);
	}

	[Test]
	public void SelectedDirectory_NestedIntoConflictingDirectory_ReportsConfiguredConflictMessage()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = new DirectoryBrowserViewModel()
		{
			ConflictingDirectoryError = DirectoryValidationError.AppDataDirectoryConflict,
			ConflictingDirectory = temp.Path,
		};

		// Act
		sut.SelectedDirectory = temp.Combine("Downloads");

		// Assert
		Assert.That(
			sut.GetErrors(nameof(DirectoryBrowserViewModel.SelectedDirectory)),
			Is.EqualTo(new[] { DirectoryValidationError.AppDataDirectoryConflict }));
	}


	[Test]
	public void SelectedDirectory_ConflictingDirectoryNestedIntoSelected_HasValidationError()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DirectoryBrowserViewModel>();
		sut.SelectedDirectory = temp.Path;

		// Act
		sut.ConflictingDirectory = temp.Combine("Skyrim");

		// Assert
		Assert.That(sut.HasErrors, Is.True);
	}

	[Test]
	public void ConflictingDirectory_ChangedToUnrelatedDirectory_ClearsValidationError()
	{
		// Arrange
		using var temp = new TempDirectory();
		using var other = new TempDirectory();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DirectoryBrowserViewModel>();
		sut.ConflictingDirectory = temp.Path;
		sut.SelectedDirectory = temp.Combine("Downloads");

		// Act
		sut.ConflictingDirectory = other.Path;

		// Assert
		Assert.That(sut.HasErrors, Is.False);
	}

	[Test]
	public void SelectedDirectory_SiblingOfConflictingDirectory_IsValid()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DirectoryBrowserViewModel>();
		sut.ConflictingDirectory = temp.Combine("Skyrim");

		// Act
		sut.SelectedDirectory = temp.Combine("SkyrimData");

		// Assert
		Assert.That(sut.HasErrors, Is.False);
	}
}

[TestFixture]
public sealed class SkyrimDirectoryBrowserViewModelTests
{
	[Test]
	public void SelectedDirectory_NonAsciiPath_HasValidationError()
	{
		// Arrange
		using var temp = new TempDirectory();
		var nonAscii = temp.CreateDirectory("Скайрим");
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<SkyrimDirectoryBrowserViewModel>();

		// Act
		sut.SelectedDirectory = nonAscii;

		// Assert
		Assert.That(sut.HasErrors, Is.True);
	}

	[Test]
	public void SelectedDirectory_ExistingAsciiPath_IsValid()
	{
		// Arrange
		using var temp = new TempDirectory();
		var path = temp.CreateDirectory("Skyrim");
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<SkyrimDirectoryBrowserViewModel>();

		// Act
		sut.SelectedDirectory = path;

		// Assert
		Assert.That(sut.HasErrors, Is.False);
	}
}
