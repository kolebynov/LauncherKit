using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Core.Tests.Utils;

[TestFixture]
public sealed class DiskSpaceCheckerTests
{
	[Test]
	public void CheckAvailableDiskSpace_RequestedFreeSpace_ThrowsWithRequiredPayload()
	{
		// Arrange
		var root = Path.GetPathRoot(Environment.SystemDirectory)!;
		var freeSpace = new DriveInfo(root).TotalFreeSpace;
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DiskSpaceChecker>();

		// Act
		var exception = Assert.Throws<NotEnoughSpaceException>(() => sut.CheckAvailableDiskSpace(root, freeSpace));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(exception!.DiskName, Is.EqualTo(root));
			Assert.That(exception.RequiredSpace, Is.EqualTo(freeSpace + 8 * 1024 * 1024));
			Assert.That(exception.AvailableFreeSpace, Is.LessThan(exception.RequiredSpace));
		});
	}

	[Test]
	public void CheckAvailableDiskSpace_ZeroBytesOnExistingDrive_DoesNotThrow()
	{
		// Arrange
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DiskSpaceChecker>();

		// Act
		// Assert
		Assert.That(() => sut.CheckAvailableDiskSpace(Environment.SystemDirectory, 0), Throws.Nothing);
	}

	[Test]
	public void CheckAvailableDiskSpace_UnknownDriveRoot_DoesNotThrow()
	{
		// Arrange
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<DiskSpaceChecker>();

		// Act
		// Assert
		Assert.That(() => sut.CheckAvailableDiskSpace(@"Z:\does-not-exist\file.bin", long.MaxValue), Throws.Nothing);
	}
}
