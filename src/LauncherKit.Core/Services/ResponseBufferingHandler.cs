namespace LauncherKit.Core.Services;

/// <summary>
/// Reads the whole response body before returning the response. Placed inside a client's resilience handler, it brings
/// the body download under its timeouts and retries: they cover only the inner handlers, so a server that sends the
/// headers and then stalls the body would otherwise never time out.
/// </summary>
internal sealed class ResponseBufferingHandler : DelegatingHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var response = await base.SendAsync(request, cancellationToken);
		try
		{
			await response.Content.LoadIntoBufferAsync(cancellationToken);
			return response;
		}
		catch
		{
			response.Dispose();
			throw;
		}
	}
}
