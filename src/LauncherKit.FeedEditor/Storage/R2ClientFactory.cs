using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace LauncherKit.FeedEditor.Storage;

public interface IR2ClientFactory
{
	IAmazonS3 Create(string accessKeyId, string secretAccessKey);
}

internal sealed class R2ClientFactory(IOptions<R2Settings> settings) : IR2ClientFactory
{
	public IAmazonS3 Create(string accessKeyId, string secretAccessKey)
	{
		var config = new AmazonS3Config
		{
			ServiceURL = settings.Value.ServiceUrl.ToString(),
			ForcePathStyle = true,
			AuthenticationRegion = "auto",
			// R2 rejects the CRC checksums newer AWS SDKs send by default.
			RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
			ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
		};

		return new AmazonS3Client(new BasicAWSCredentials(accessKeyId, secretAccessKey), config);
	}
}
