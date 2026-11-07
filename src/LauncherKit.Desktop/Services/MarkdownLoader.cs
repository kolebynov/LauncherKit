using LauncherKit.Core;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Services;

/// <summary>Downloads documents with the feed's HTTP client, so they get the feed server's headers and retries.</summary>
internal sealed class MarkdownLoader : IMarkdownLoader
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger<MarkdownLoader> _logger;

	public MarkdownLoader(IHttpClientFactory httpClientFactory, ILogger<MarkdownLoader> logger)
	{
		_httpClientFactory = httpClientFactory;
		_logger = logger;
	}

	public async Task<string?> TryLoad(Uri source, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		try
		{
			using var httpClient = _httpClientFactory.CreateClient(AppConstants.S3HttpClient);
			return await httpClient.GetStringAsync(source, cancellationToken);
		}
		catch (Exception e) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(e, "Failed to load Markdown document {Source}", source);
			return null;
		}
	}
}
