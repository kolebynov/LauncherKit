using System.ComponentModel.DataAnnotations;

namespace LauncherKit.FeedEditor.Storage;

/// <summary>Location of the feed object in Cloudflare R2. Credentials are never configured; each user signs in with their own.</summary>
public sealed class R2Settings
{
	public const string SectionName = "R2";

	/// <summary>S3 API endpoint, e.g. <c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c>.</summary>
	[Required]
	public required Uri ServiceUrl { get; set; }

	[Required]
	public required string BucketName { get; set; }

	[Required]
	public required string FeedKey { get; set; }

	[Required]
	public string LegacyFeedKey { get; set; } = "feedV11.json";
}
