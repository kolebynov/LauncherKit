using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.FeedEditor.Feed.Json;
using LauncherKit.FeedEditor.Storage;
using LauncherKit.TestSupport;

namespace LauncherKit.FeedEditor.Tests.Storage;

[TestFixture]
public sealed class FeedSessionTests
{
	private const string Bucket = "stb";
	private const string FeedKey = "feedV11.json";
	private const string ETag = "\"etag-1\"";

	[Test]
	public async Task SignInAsync_CredentialsRejected_ReturnsInvalidCredentialsAndStaysSignedOut()
	{
		// Arrange
		var mocker = CreateMocker();
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.Forbidden));
		client.Setup(x => x.Dispose());
		using var sut = mocker.CreateInstance<FeedSession>();

		// Act
		var result = await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(SignInResult.InvalidCredentials));
			Assert.That(sut.IsSignedIn, Is.False);
			Assert.That(sut.Feed, Is.Null);
		});
		client.Verify(x => x.Dispose(), Times.Once);
	}

	[Test]
	public async Task SignInAsync_FeedMissing_ThrowsStorageExceptionNamingFeed()
	{
		// Arrange
		var mocker = CreateMocker();
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.NotFound));
		client.Setup(x => x.Dispose());
		using var sut = mocker.CreateInstance<FeedSession>();

		// Act & Assert
		Assert.That(
			async () => await sut.SignInAsync("key", "secret", CancellationToken.None),
			Throws.TypeOf<FeedStorageException>().With.Message.Contains($"{Bucket}/{FeedKey}"));
		Assert.That(sut.IsSignedIn, Is.False);
	}

	[Test]
	public async Task SignInAsync_ValidCredentials_LoadsConfiguredFeedWithoutChanges()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		using var sut = mocker.CreateInstance<FeedSession>();

		// Act
		var result = await sut.SignInAsync(" key ", " secret ", CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(SignInResult.SignedIn));
			Assert.That(sut.Feed!.Launchers[0].Version, Is.EqualTo("1.19.1"));
			Assert.That(sut.HasUnsavedChanges, Is.False);
		});
		mocker.GetMock<IR2ClientFactory>().Verify(x => x.Create("key", "secret"), Times.Once);
	}

	[Test]
	public async Task SignInAsync_BucketListedInPages_ExposesAllFileKeysSortedWithoutFolderMarkers()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.ListObjectsV2Async(
				It.Is<ListObjectsV2Request>(r => r.BucketName == Bucket && r.ContinuationToken == null), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Listing(["Mods/b.7z", "Mods/", FeedKey], nextToken: "page-2"));
		client.Setup(x => x.ListObjectsV2Async(
				It.Is<ListObjectsV2Request>(r => r.BucketName == Bucket && r.ContinuationToken == "page-2"), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Listing(["Mods/a.md", "Launcher/1.19.1.exe"], nextToken: null));
		using var sut = mocker.CreateInstance<FeedSession>();

		// Act
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Assert
		Assert.That(sut.ObjectKeys, Is.EqualTo(new[] { "Launcher/1.19.1.exe", "Mods/a.md", "Mods/b.7z", FeedKey }));
	}

	[Test]
	public async Task SignInAsync_ListingRefused_ThrowsStorageExceptionNamingBucketAndStaysSignedOut()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.Forbidden));
		using var sut = mocker.CreateInstance<FeedSession>();

		// Act & Assert
		Assert.That(
			async () => await sut.SignInAsync("key", "secret", CancellationToken.None),
			Throws.TypeOf<FeedStorageException>().With.Message.Contains($"бакете {Bucket}"));
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsSignedIn, Is.False);
			Assert.That(sut.Feed, Is.Null);
		});
	}

	[Test]
	public async Task RefreshObjectKeysAsync_FileUploadedAfterSignIn_AddsKeyAndKeepsEdits()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		var client = mocker.GetMock<IAmazonS3>();
		client.SetupSequence(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Listing([FeedKey], nextToken: null))
			.ReturnsAsync(Listing([FeedKey, "Changelogs/new.md"], nextToken: null));
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		sut.Feed!.Launchers[0].ChangelogPath = new Dictionary<string, string> { ["ru"] = "Changelogs/new.md" };

		// Act
		await sut.RefreshObjectKeysAsync(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.ObjectKeys, Is.EqualTo(new[] { "Changelogs/new.md", FeedKey }));
			Assert.That(sut.Feed.Launchers[0].ChangelogPath!["ru"], Is.EqualTo("Changelogs/new.md"));
			Assert.That(sut.HasUnsavedChanges, Is.True);
		});
	}

	[Test]
	public async Task ReloadAsync_CredentialsRevokedAfterSignIn_ThrowsStorageExceptionAndKeepsEdits()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		sut.Feed!.Launchers[0].Version = "9.9.9";
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.Forbidden));

		// Act & Assert
		Assert.That(
			async () => await sut.ReloadAsync(CancellationToken.None),
			Throws.TypeOf<FeedStorageException>().With.Message.Contains($"{Bucket}/{FeedKey}"));
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsSignedIn, Is.True);
			Assert.That(sut.Feed.Launchers[0].Version, Is.EqualTo("9.9.9"));
		});
	}

	[Test]
	public async Task SaveAsync_EditedFeed_UploadsConditionallyAndKeepsObjectHeaders()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: "max-age=60");
		PutObjectRequest? request = null;
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
			.Callback<PutObjectRequest, CancellationToken>((r, _) => request = r)
			.ReturnsAsync(new PutObjectResponse { ETag = "\"etag-2\"" });
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		sut.Feed!.Launchers[0].ChangelogPath = new Dictionary<string, string> { ["ru"] = "Changelogs/Launcher/1.19.1.md" };

		// Act
		var result = await sut.SaveAsync(overwrite: false, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(SaveResult.Saved));
			Assert.That(request!.BucketName, Is.EqualTo(Bucket));
			Assert.That(request.Key, Is.EqualTo(FeedKey));
			Assert.That(request.IfMatch, Is.EqualTo(ETag));
			Assert.That(request.ContentType, Is.EqualTo("application/json; charset=utf-8"));
			Assert.That(request.Headers.CacheControl, Is.EqualTo("max-age=60"));
			Assert.That(FeedJson.Deserialize(request.ContentBody).Launchers[0].ChangelogPath!["ru"], Is.EqualTo("Changelogs/Launcher/1.19.1.md"));
			Assert.That(sut.HasUnsavedChanges, Is.False);
		});
	}

	[Test]
	public async Task SaveAsync_StoredFeedChangedMeanwhile_ReturnsConflictAndKeepsChanges()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.PreconditionFailed));
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		sut.Feed!.Launchers.RemoveAt(0);

		// Act
		var result = await sut.SaveAsync(overwrite: false, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(SaveResult.Conflict));
			Assert.That(sut.HasUnsavedChanges, Is.True);
		});
	}

	[Test]
	public async Task SaveAsync_Overwrite_UploadsWithoutPrecondition()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		PutObjectRequest? request = null;
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
			.Callback<PutObjectRequest, CancellationToken>((r, _) => request = r)
			.ReturnsAsync(new PutObjectResponse { ETag = "\"etag-2\"" });
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Act
		var result = await sut.SaveAsync(overwrite: true, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(SaveResult.Saved));
			Assert.That(request!.IfMatch, Is.Null);
		});
	}

	[Test]
	public async Task SaveAsync_InvalidFeed_ThrowsWithoutUploading()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		sut.Feed!.Launchers[0].Version = "latest";

		// Act & Assert
		Assert.That(async () => await sut.SaveAsync(overwrite: false, CancellationToken.None), Throws.InvalidOperationException);
		mocker.GetMock<IAmazonS3>().Verify(
			x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Test]
	public async Task StartUploadAsync_FileUpToPartSize_ReturnsSinglePutCoveringWholeFile()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Act
		var upload = await sut.StartUploadAsync("Logos/new.png", FeedSession.UploadPartSize, "image/png", CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(upload.UploadId, Is.Null);
			Assert.That(upload.ContentType, Is.EqualTo("image/png"));
			Assert.That(upload.Parts, Is.EqualTo(new[] { new UploadPart(1, 0, FeedSession.UploadPartSize) }));
		});
	}

	[Test]
	public async Task StartUploadAsync_FileLargerThanPartSize_StartsMultipartUploadWithConsecutiveEqualParts()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		const string Key = "Mods/big.7z";
		const long Size = FeedSession.UploadPartSize;
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.InitiateMultipartUploadAsync(
				It.Is<InitiateMultipartUploadRequest>(r => r.BucketName == Bucket && r.Key == Key && r.ContentType == null),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Act
		var upload = await sut.StartUploadAsync(Key, (2 * Size) + 5, contentType: "", CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(upload.UploadId, Is.EqualTo("upload-1"));
			Assert.That(upload.Parts, Is.EqualTo(new[] { new UploadPart(1, 0, Size), new UploadPart(2, Size, Size), new UploadPart(3, 2 * Size, 5) }));
		});
	}

	[Test]
	public async Task StartUploadAsync_MultipartUploadRefused_ThrowsStorageExceptionNamingObject()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.Forbidden));
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Act & Assert
		Assert.That(
			async () => await sut.StartUploadAsync("Mods/big.7z", FeedSession.UploadPartSize + 1, null, CancellationToken.None),
			Throws.TypeOf<FeedStorageException>().With.Message.Contains($"{Bucket}/Mods/big.7z"));
	}

	[Test]
	public async Task GetUploadPartUrlAsync_SinglePut_SignsPutOfObjectWithItsContentType()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		GetPreSignedUrlRequest? request = null;
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
			.Callback<GetPreSignedUrlRequest>(r => request = r)
			.ReturnsAsync("https://signed");
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		var upload = await sut.StartUploadAsync("Logos/new.png", 3, "image/png", CancellationToken.None);

		// Act
		var url = await sut.GetUploadPartUrlAsync(upload, 1);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(url, Is.EqualTo("https://signed"));
			Assert.That((request!.BucketName, request.Key, request.Verb), Is.EqualTo((Bucket, "Logos/new.png", HttpVerb.PUT)));
			Assert.That(request.ContentType, Is.EqualTo("image/png"));
			Assert.That(request.UploadId, Is.Null);
			Assert.That(request.Expires, Is.GreaterThan(DateTime.UtcNow));
		});
	}

	[Test]
	public async Task GetUploadPartUrlAsync_MultipartUpload_SignsPutOfThatPart()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
		GetPreSignedUrlRequest? request = null;
		client.Setup(x => x.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
			.Callback<GetPreSignedUrlRequest>(r => request = r)
			.ReturnsAsync("https://signed");
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		var upload = await sut.StartUploadAsync("Mods/big.7z", FeedSession.UploadPartSize + 1, "application/x-7z-compressed", CancellationToken.None);

		// Act
		await sut.GetUploadPartUrlAsync(upload, 2);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That((request!.Key, request.Verb, request.UploadId, request.PartNumber), Is.EqualTo(("Mods/big.7z", HttpVerb.PUT, "upload-1", 2)));
			// The part requests carry no content type; it was set when the upload started.
			Assert.That(request.ContentType, Is.Null);
		});
	}

	[Test]
	public async Task CompleteUploadAsync_MultipartUpload_CompletesWithPartETagsAndAddsKeyInOrder()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
		CompleteMultipartUploadRequest? complete = null;
		client.Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
			.Callback<CompleteMultipartUploadRequest, CancellationToken>((r, _) => complete = r)
			.ReturnsAsync(new CompleteMultipartUploadResponse());
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		var upload = await sut.StartUploadAsync("Big.7z", FeedSession.UploadPartSize + 1, null, CancellationToken.None);

		// Act
		await sut.CompleteUploadAsync(upload, ["\"part-1\"", "\"part-2\""], CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That((complete!.BucketName, complete.Key, complete.UploadId), Is.EqualTo((Bucket, "Big.7z", "upload-1")));
			Assert.That(complete.PartETags.Select(x => (x.PartNumber, x.ETag)), Is.EqualTo(new[] { (1, "\"part-1\""), (2, "\"part-2\"") }));
			Assert.That(sut.ObjectKeys, Is.EqualTo(new[] { "Big.7z", FeedKey }));
		});
	}

	[Test]
	public async Task CompleteUploadAsync_PartETagNotExposed_ThrowsStorageExceptionNamingCorsWithoutCompletingOrAddingKey()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		var upload = await sut.StartUploadAsync("Big.7z", FeedSession.UploadPartSize + 1, null, CancellationToken.None);

		// Act & Assert
		Assert.That(
			async () => await sut.CompleteUploadAsync(upload, ["\"part-1\"", null], CancellationToken.None),
			Throws.TypeOf<FeedStorageException>().With.Message.Contains("CORS"));
		client.Verify(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		Assert.That(sut.ObjectKeys, Is.EqualTo(new[] { FeedKey }));
	}

	[Test]
	public async Task CompleteUploadAsync_SinglePut_AddsKeyWithoutRequests()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		var upload = await sut.StartUploadAsync("Logos/new.png", 3, "image/png", CancellationToken.None);

		// Act
		await sut.CompleteUploadAsync(upload, [null], CancellationToken.None);

		// Assert
		Assert.That(sut.ObjectKeys, Is.EqualTo(new[] { "Logos/new.png", FeedKey }));
	}

	[Test]
	public async Task AbortUploadAsync_AbortRefused_AbortsUploadWithoutThrowing()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
		client.Setup(x => x.AbortMultipartUploadAsync(
				It.Is<AbortMultipartUploadRequest>(r => r.BucketName == Bucket && r.Key == "Big.7z" && r.UploadId == "upload-1"),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.InternalServerError));
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);
		var upload = await sut.StartUploadAsync("Big.7z", FeedSession.UploadPartSize + 1, null, CancellationToken.None);

		// Act & Assert
		Assert.That(async () => await sut.AbortUploadAsync(upload), Throws.Nothing);
		client.Verify(x => x.AbortMultipartUploadAsync(It.IsAny<AbortMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Test]
	public async Task GetObjectSha256Async_LowercaseHexMetadata_ReturnsUppercaseHex()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		SetupObjectMetadata(mocker, "Mods/a.7z", "18a4e92e69742cd42c53e47433009db5e6f1345c9c2397ba1f053437afa80d74");
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Act
		var result = await sut.GetObjectSha256Async("Mods/a.7z", CancellationToken.None);

		// Assert
		Assert.That(result, Is.EqualTo("18A4E92E69742CD42C53E47433009DB5E6F1345C9C2397BA1F053437AFA80D74"));
	}

	[TestCase(null, TestName = "GetObjectSha256Async_NoMetadata_ReturnsNull")]
	[TestCase("18a4e92e69742cd42c53e47433009db5", TestName = "GetObjectSha256Async_ShorterHash_ReturnsNull")]
	[TestCase("GKTpLml0LNQsU+R0MwCdtebxNFycI5e6HwU0N6+oDXQ=", TestName = "GetObjectSha256Async_NotHex_ReturnsNull")]
	public async Task GetObjectSha256Async_NoSha256Metadata_ReturnsNull(string? metadata)
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		SetupObjectMetadata(mocker, "Mods/a.7z", metadata);
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Act
		var result = await sut.GetObjectSha256Async("Mods/a.7z", CancellationToken.None);

		// Assert
		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task GetObjectSha256Async_RequestRefused_ReturnsNull()
	{
		// Arrange
		var mocker = CreateMocker();
		SetupStoredFeed(mocker, cacheControl: null);
		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(S3Error(HttpStatusCode.Forbidden));
		using var sut = mocker.CreateInstance<FeedSession>();
		await sut.SignInAsync("key", "secret", CancellationToken.None);

		// Act
		var result = await sut.GetObjectSha256Async("Mods/a.7z", CancellationToken.None);

		// Assert
		Assert.That(result, Is.Null);
	}

	private static void SetupObjectMetadata(AutoMocker mocker, string key, string? sha256Metadata)
	{
		var response = new GetObjectMetadataResponse();
		if (sha256Metadata is not null)
		{
			response.Metadata.Add("x-amz-meta-checksum-sha256", sha256Metadata);
		}

		mocker.GetMock<IAmazonS3>()
			.Setup(x => x.GetObjectMetadataAsync(
				It.Is<GetObjectMetadataRequest>(r => r.BucketName == Bucket && r.Key == key), It.IsAny<CancellationToken>()))
			.ReturnsAsync(response);
	}

	private static AutoMocker CreateMocker()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		OptionsStubs.Options(mocker, new R2Settings { ServiceUrl = new Uri("https://account.r2.test"), BucketName = Bucket, FeedKey = FeedKey });
		mocker.Use<ILogger<FeedSession>>(NullLogger<FeedSession>.Instance);
		mocker.GetMock<IR2ClientFactory>()
			.Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>()))
			.Returns(mocker.GetMock<IAmazonS3>().Object);
		return mocker;
	}

	private static void SetupStoredFeed(AutoMocker mocker, string? cacheControl)
	{
		var client = mocker.GetMock<IAmazonS3>();
		client.Setup(x => x.GetObjectAsync(
				It.Is<GetObjectRequest>(r => r.BucketName == Bucket && r.Key == FeedKey), It.IsAny<CancellationToken>()))
			.ReturnsAsync(() =>
			{
				var response = new GetObjectResponse
				{
					ETag = ETag,
					ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(FeedFixtures.StoredFeedJson())),
				};
				response.Headers.ContentType = "application/json; charset=utf-8";
				response.Headers.CacheControl = cacheControl;
				return response;
			});
		client.Setup(x => x.Dispose());
		client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(() => Listing([FeedKey], nextToken: null));
	}

	private static ListObjectsV2Response Listing(IReadOnlyList<string> keys, string? nextToken) => new()
	{
		S3Objects = [.. keys.Select(key => new S3Object { Key = key })],
		IsTruncated = nextToken is not null,
		NextContinuationToken = nextToken,
	};

	private static AmazonS3Exception S3Error(HttpStatusCode statusCode) =>
		new(statusCode.ToString(), ErrorType.Sender, statusCode.ToString(), "request-id", statusCode);
}
