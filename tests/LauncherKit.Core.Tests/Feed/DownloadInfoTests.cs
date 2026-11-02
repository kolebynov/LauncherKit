using LauncherKit.Core.Feed;

namespace LauncherKit.Core.Tests.Feed;

[TestFixture]
public sealed class DownloadInfoTests
{
	[TestCase("folder/archive.7z", "archive.7z")]
	[TestCase("archive.7z", "archive.7z")]
	[TestCase("folder/", "")]
	public void FileName_S3Path_ReturnsFinalPathSegment(string s3Path, string expected)
	{
		// Arrange
		var sut = new DownloadInfo { S3Path = s3Path };

		// Act
		var result = sut.FileName;

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}
}
