using System.Net;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using LauncherKit.FeedEditor.Feed;
using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Storage;

public enum SignInResult
{
	SignedIn,
	InvalidCredentials,
}

public enum SaveResult
{
	Saved,

	/// <summary>The stored feed changed since it was loaded; nothing was written.</summary>
	Conflict,
}

/// <summary>A storage failure with a message that can be shown to the user as is.</summary>
public sealed class FeedStorageException : Exception
{
	public FeedStorageException()
	{
	}

	public FeedStorageException(string message)
		: base(message)
	{
	}

	public FeedStorageException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

/// <summary>
/// The signed-in user's R2 client and the feed being edited. Scoped, so in Blazor Server each browser circuit has
/// its own session and the credentials never leave server memory.
/// </summary>
public sealed class FeedSession(IR2ClientFactory clientFactory, IOptions<R2Settings> settings, ILogger<FeedSession> logger)
	: IDisposable
{
	/// <summary>
	/// Size of every part of a multipart upload except the last; a file up to this size is uploaded in one request.
	/// R2 requires equal-sized parts and limits the size of a single upload request, so big files are sent in parts,
	/// which the browser also sends several at a time.
	/// </summary>
	public const long UploadPartSize = 64L * 1024 * 1024;

	/// <summary>
	/// How long a presigned part URL stays valid. R2 checks it when the request starts, and each URL is requested just
	/// before its part is sent, so it only has to outlive the wait for the request to start.
	/// </summary>
	private static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromHours(1);

	private const string DefaultContentType = "application/json";

	/// <summary>User metadata (<c>x-amz-meta-checksum-sha256</c>) holding the SHA-256 of an object's whole file.</summary>
	private const string Sha256MetadataKey = "x-amz-meta-checksum-sha256";

	private const int Sha256Length = 32;

	private readonly R2Settings _settings = settings.Value;
	private IAmazonS3? _client;
	private StoredFeed? _stored;

	public FeedModel? Feed { get; private set; }

	public IReadOnlyList<FeedValidationError> Validate() => FeedValidator.Validate(Feed!);

	public string Serialize() => FeedJson.Serialize(Feed!);

	/// <summary>
	/// Keys of every object in the bucket, sorted ordinally; feed paths are relative to the bucket root, so these are
	/// the values a path field can refer to. Loaded with the feed and refreshed by <see cref="RefreshObjectKeysAsync"/>.
	/// </summary>
	public IReadOnlyList<string> ObjectKeys { get; private set; } = [];

	public bool IsSignedIn => _client is not null;

	public string FeedLocation => $"{_settings.BucketName}/{_settings.FeedKey}";

	/// <summary>Whether the feed differs from what was last loaded or saved, compared by its serialized form.</summary>
	public bool HasUnsavedChanges => Feed is not null && _stored is not null && Serialize() != _stored.Json;

	/// <exception cref="FeedStorageException">R2 is unreachable or the stored feed cannot be read.</exception>
	public async Task<SignInResult> SignInAsync(string accessKeyId, string secretAccessKey, CancellationToken cancellationToken)
	{
		if (IsSignedIn)
		{
			throw new InvalidOperationException("The session is already signed in.");
		}

		var client = clientFactory.Create(accessKeyId.Trim(), secretAccessKey.Trim());
		try
		{
			await LoadAsync(client, cancellationToken);
		}
		catch (AmazonServiceException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			client.Dispose();
			logger.LogWarning("R2 rejected sign-in for access key {AccessKeyId}: {ErrorCode}", accessKeyId, e.ErrorCode);
			return SignInResult.InvalidCredentials;
		}
		catch
		{
			client.Dispose();
			throw;
		}

		_client = client;
		logger.LogInformation("Access key {AccessKeyId} signed in to edit {FeedLocation}", accessKeyId, FeedLocation);
		return SignInResult.SignedIn;
	}

	/// <summary>Replaces the edited feed with the stored one, discarding unsaved changes.</summary>
	/// <exception cref="FeedStorageException">
	/// R2 is unreachable, rejected the credentials (e.g. a revoked token), or the stored feed cannot be read.
	/// </exception>
	public async Task ReloadAsync(CancellationToken cancellationToken)
	{
		try
		{
			await LoadAsync(GetClient(), cancellationToken);
		}
		catch (AmazonServiceException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			// LoadAsync leaves these unwrapped so sign-in can report invalid credentials; here they mean the token
			// stopped working after sign-in.
			throw new FeedStorageException($"R2 отклонил ключи доступа при загрузке {FeedLocation}; войдите заново.", e);
		}
	}

	/// <summary>Re-reads the bucket's object keys, keeping the edited feed.</summary>
	/// <exception cref="FeedStorageException">R2 is unreachable or refused the listing.</exception>
	public async Task RefreshObjectKeysAsync(CancellationToken cancellationToken) =>
		ObjectKeys = await ListObjectKeysAsync(GetClient(), cancellationToken);

	/// <summary>
	/// Uploads the feed. Unless <paramref name="overwrite"/> is set, the upload succeeds only if the stored feed is
	/// still the version that was loaded, so concurrent editors cannot silently overwrite each other.
	/// </summary>
	/// <exception cref="InvalidOperationException">The feed has validation errors.</exception>
	/// <exception cref="FeedStorageException">R2 rejected or failed the upload.</exception>
	public async Task<SaveResult> SaveAsync(bool overwrite, CancellationToken cancellationToken)
	{
		var client = GetClient();
		var stored = _stored!;
		if (Validate().Count > 0)
		{
			throw new InvalidOperationException("A feed with validation errors must not be saved.");
		}

		var json = Serialize();
		var request = new PutObjectRequest
		{
			BucketName = _settings.BucketName,
			Key = _settings.FeedKey,
			ContentBody = json,
			ContentType = stored.ContentType ?? DefaultContentType,
			IfMatch = overwrite ? null : stored.ETag,
			// R2 does not support streaming (chunked) SigV4 uploads; the feed is small enough to sign whole.
			UseChunkEncoding = false,
		};
		request.Headers.CacheControl = stored.CacheControl;

		PutObjectResponse response;
		try
		{
			response = await client.PutObjectAsync(request, cancellationToken);
		}
		catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
		{
			logger.LogWarning("Feed {FeedLocation} changed since it was loaded (ETag {ETag})", FeedLocation, stored.ETag);
			return SaveResult.Conflict;
		}
		catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException)
		{
			throw new FeedStorageException($"Не удалось сохранить {FeedLocation}: {e.Message}", e);
		}

		_stored = stored with { Json = json, ETag = response.ETag };
		logger.LogInformation("Saved feed {FeedLocation} (ETag {ETag}, overwrite: {Overwrite})", FeedLocation, response.ETag, overwrite);
		return SaveResult.Saved;
	}

	/// <summary>
	/// Starts uploading a <paramref name="length"/>-byte file to the bucket as <paramref name="key"/>, replacing an
	/// existing object. The browser then sends each part to the URL from <see cref="GetUploadPartUrlAsync"/>, so the
	/// file never passes through the server, and the upload ends with <see cref="CompleteUploadAsync"/> or
	/// <see cref="AbortUploadAsync"/>. A file bigger than <see cref="UploadPartSize"/> starts a multipart upload.
	/// </summary>
	/// <exception cref="FeedStorageException">R2 refused to start the multipart upload.</exception>
	public async Task<ObjectUpload> StartUploadAsync(string key, long length, string? contentType, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		var client = GetClient();
		contentType = string.IsNullOrEmpty(contentType) ? null : contentType;
		if (length <= UploadPartSize)
		{
			return new ObjectUpload(key, length, contentType, UploadId: null, [new UploadPart(1, 0, length)]);
		}

		InitiateMultipartUploadResponse response;
		try
		{
			response = await client.InitiateMultipartUploadAsync(
				new InitiateMultipartUploadRequest { BucketName = _settings.BucketName, Key = key, ContentType = contentType },
				cancellationToken);
		}
		catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException or IOException)
		{
			throw UploadFailed(key, e);
		}

		var partCount = (int)((length + UploadPartSize - 1) / UploadPartSize);
		var parts = new UploadPart[partCount];
		for (var i = 0; i < partCount; i++)
		{
			var offset = i * UploadPartSize;
			parts[i] = new UploadPart(i + 1, offset, Math.Min(UploadPartSize, length - offset));
		}

		return new ObjectUpload(key, length, contentType, response.UploadId, parts);
	}

	/// <summary>
	/// A presigned URL the browser sends part <paramref name="partNumber"/> of <paramref name="upload"/> to with
	/// <c>PUT</c>. The bucket's CORS rules must allow the editor's origin to <c>PUT</c> and expose the <c>ETag</c>
	/// response header, which <see cref="CompleteUploadAsync"/> needs for every part of a multipart upload.
	/// </summary>
	/// <exception cref="FeedStorageException">The URL could not be signed.</exception>
	public async Task<string> GetUploadPartUrlAsync(ObjectUpload upload, int partNumber)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(partNumber, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(partNumber, upload.Parts.Count);
		var request = new GetPreSignedUrlRequest
		{
			BucketName = _settings.BucketName,
			Key = upload.Key,
			Verb = HttpVerb.PUT,
			Expires = DateTime.UtcNow.Add(UploadUrlLifetime),
			// The SDK signs https URLs unless told otherwise; follow the configured endpoint (e.g. a local S3 server).
			Protocol = _settings.ServiceUrl.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
		};
		if (upload.UploadId is null)
		{
			request.ContentType = upload.ContentType;
		}
		else
		{
			request.UploadId = upload.UploadId;
			request.PartNumber = partNumber;
		}

		try
		{
			return await GetClient().GetPreSignedURLAsync(request);
		}
		catch (AmazonClientException e)
		{
			throw UploadFailed(upload.Key, e);
		}
	}

	/// <summary>
	/// Finishes <paramref name="upload"/> once the browser has sent every part, and adds its key to
	/// <see cref="ObjectKeys"/>. On failure the caller aborts the upload, as for a failed part.
	/// </summary>
	/// <param name="partETags">The <c>ETag</c> R2 returned for each part, in part order.</param>
	/// <exception cref="FeedStorageException">
	/// R2 rejected the completion or a part's <c>ETag</c> is missing because the bucket's CORS rules do not expose it.
	/// </exception>
	public async Task CompleteUploadAsync(ObjectUpload upload, IReadOnlyList<string?> partETags, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNotEqual(partETags.Count, upload.Parts.Count);
		var client = GetClient();
		if (upload.UploadId is { } uploadId)
		{
			if (partETags.Any(string.IsNullOrEmpty))
			{
				throw new FeedStorageException(
					$"R2 не вернул ETag частей {_settings.BucketName}/{upload.Key}: добавьте ETag в ExposeHeaders CORS-правил бакета.");
			}

			try
			{
				await client.CompleteMultipartUploadAsync(
					new CompleteMultipartUploadRequest
					{
						BucketName = _settings.BucketName,
						Key = upload.Key,
						UploadId = uploadId,
						PartETags = [.. upload.Parts.Select(x => new PartETag(x.Number, partETags[x.Number - 1]))],
					},
					cancellationToken);
			}
			catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException or IOException)
			{
				throw UploadFailed(upload.Key, e);
			}
		}

		if (!ObjectKeys.Contains(upload.Key, StringComparer.Ordinal))
		{
			// A new list rather than an insert, so a render enumerating the current keys is not disturbed.
			ObjectKeys = [.. ObjectKeys.Append(upload.Key).Order(StringComparer.Ordinal)];
		}

		logger.LogInformation("Uploaded {Length} bytes to {Bucket}/{Key}", upload.Length, _settings.BucketName, upload.Key);
	}

	/// <summary>
	/// Discards a failed or cancelled upload. R2 drops the parts of an aborted multipart upload; a single <c>PUT</c>
	/// has nothing to discard. Failures are only logged, since the upload has already failed.
	/// </summary>
	public async Task AbortUploadAsync(ObjectUpload upload)
	{
		if (upload.UploadId is not { } uploadId || _client is not { } client)
		{
			return;
		}

		try
		{
			// No caller token: a cancelled upload must still be aborted.
			await client.AbortMultipartUploadAsync(
				new AbortMultipartUploadRequest { BucketName = _settings.BucketName, Key = upload.Key, UploadId = uploadId },
				CancellationToken.None);
		}
		catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException or IOException)
		{
			// R2 also removes abandoned multipart uploads after a week.
			logger.LogWarning(e, "Failed to abort the multipart upload {UploadId} of {Bucket}/{Key}", uploadId, _settings.BucketName, upload.Key);
		}
	}

	/// <summary>
	/// The SHA-256 (uppercase hex, the form feed hashes use) of the object <paramref name="key"/> from its
	/// <c>x-amz-meta-checksum-sha256</c> user metadata (hex, e.g. set by the tool that uploaded it), or <c>null</c>
	/// when the object has none or it is not a SHA-256. Unlike R2's own checksums, which for multipart uploads cover
	/// only the parts, it is the whole file's hash. The hash is a convenience, so a failed request is only logged.
	/// </summary>
	public async Task<string?> GetObjectSha256Async(string key, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		GetObjectMetadataResponse response;
		try
		{
			response = await GetClient().GetObjectMetadataAsync(
				new GetObjectMetadataRequest { BucketName = _settings.BucketName, Key = key }, cancellationToken);
		}
		catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException or IOException)
		{
			logger.LogWarning(e, "Failed to read the metadata of {Bucket}/{Key}", _settings.BucketName, key);
			return null;
		}

		var value = response.Metadata[Sha256MetadataKey]?.Trim();
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}

		Span<byte> hash = stackalloc byte[Sha256Length];
		if (value.Length == Sha256Length * 2 && Convert.FromHexString(value, hash, out _, out var written) == System.Buffers.OperationStatus.Done
			&& written == Sha256Length)
		{
			return Convert.ToHexString(hash);
		}

		logger.LogWarning("Metadata {MetadataKey} of {Bucket}/{Key} is not a SHA-256: {Value}", Sha256MetadataKey, _settings.BucketName, key, value);
		return null;
	}

	public void SignOut()
	{
		_client?.Dispose();
		_client = null;
		_stored = null;
		Feed = null;
		ObjectKeys = [];
	}

	public void Dispose() => _client?.Dispose();

	private IAmazonS3 GetClient() => _client ?? throw new InvalidOperationException("The session is not signed in.");

	private async Task LoadAsync(IAmazonS3 client, CancellationToken cancellationToken)
	{
		string content;
		StoredFeed stored;
		try
		{
			using var response = await client.GetObjectAsync(
				new GetObjectRequest { BucketName = _settings.BucketName, Key = _settings.FeedKey }, cancellationToken);
			using var reader = new StreamReader(response.ResponseStream);
			content = await reader.ReadToEndAsync(cancellationToken);
			stored = new StoredFeed(string.Empty, response.ETag, response.Headers.ContentType, response.Headers.CacheControl);
		}
		catch (AmazonServiceException e) when (e.StatusCode == HttpStatusCode.NotFound)
		{
			throw new FeedStorageException($"Фид {FeedLocation} не существует.", e);
		}
		catch (AmazonServiceException e) when (e.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
		{
			throw new FeedStorageException($"Не удалось загрузить {FeedLocation}: {e.Message}", e);
		}
		catch (Exception e) when (e is AmazonClientException or HttpRequestException)
		{
			throw new FeedStorageException($"Не удалось загрузить {FeedLocation}: {e.Message}", e);
		}

		FeedModel feed;
		try
		{
			feed = FeedJson.Deserialize(content);
		}
		catch (JsonException e)
		{
			throw new FeedStorageException($"Фид {FeedLocation} содержит некорректные данные: {e.Message}", e);
		}

		var objectKeys = await ListObjectKeysAsync(client, cancellationToken);
		Feed = feed;
		ObjectKeys = objectKeys;
		_stored = stored with { Json = Serialize() };
	}

	private FeedStorageException UploadFailed(string key, Exception e) =>
		new($"Не удалось загрузить файл {_settings.BucketName}/{key}: {e.Message}", e);

	private async Task<IReadOnlyList<string>> ListObjectKeysAsync(IAmazonS3 client, CancellationToken cancellationToken)
	{
		var keys = new List<string>();
		var request = new ListObjectsV2Request { BucketName = _settings.BucketName };
		try
		{
			ListObjectsV2Response response;
			do
			{
				response = await client.ListObjectsV2Async(request, cancellationToken);
				// Keys ending in '/' are folder placeholders created by some S3 clients, not files a feed can point to.
				keys.AddRange(response.S3Objects?.Select(x => x.Key).Where(x => !x.EndsWith('/', StringComparison.Ordinal)) ?? []);
				request.ContinuationToken = response.NextContinuationToken;
			}
			while (response.IsTruncated == true);
		}
		catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException)
		{
			throw new FeedStorageException($"Не удалось получить список файлов в бакете {_settings.BucketName}: {e.Message}", e);
		}

		keys.Sort(StringComparer.Ordinal);
		return keys;
	}

	/// <param name="Json">The feed as this editor serializes it, so unchanged feeds compare equal.</param>
	private sealed record StoredFeed(string Json, string ETag, string? ContentType, string? CacheControl);
}
