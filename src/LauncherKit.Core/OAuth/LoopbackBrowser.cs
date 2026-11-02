using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.OAuth;

/// <summary>
/// OAuth browser of the native-app flow (RFC 8252): listens on the loopback redirect URI, opens the authorization
/// page in the user's default browser and returns the first request to the redirect path, which carries the
/// authorization response. Requests to other paths (e.g. <c>/favicon.ico</c>) get 404 and are ignored.
/// </summary>
internal sealed class LoopbackBrowser : IBrowser
{
	private const int MaxRequestHeadLength = 16 * 1024;
	private readonly IOptionsFactory<LoopbackBrowserOptions> _optionsFactory;
	private const string CompletedPageTemplate =
		"""
		<!DOCTYPE html>
		<html>
		<head><meta charset="utf-8"><title>{0}</title></head>
		<body style="font-family: sans-serif; text-align: center; margin-top: 15%">
		<h2>{0}</h2>
		<p>{1}</p>
		</body>
		</html>
		""";
	private static readonly CompositeFormat CompletedPageFormat = CompositeFormat.Parse(CompletedPageTemplate);

	private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(10);

	private readonly IExternalBrowser _externalBrowser;
	private readonly ILogger<LoopbackBrowser> _logger;

	public LoopbackBrowser(IExternalBrowser externalBrowser, ILogger<LoopbackBrowser> logger, IOptionsFactory<LoopbackBrowserOptions> optionsFactory)
	{
		_externalBrowser = externalBrowser;
		_logger = logger;
		_optionsFactory = optionsFactory;
	}

	public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		var redirectUri = new Uri(options.EndUrl);
		if (redirectUri.Scheme != Uri.UriSchemeHttp || !IPAddress.TryParse(redirectUri.Host, out var address) ||
		    !IPAddress.IsLoopback(address))
		{
			throw new ArgumentException($"Redirect URI {redirectUri} is not an http loopback IP address", nameof(options));
		}

		using var listener = new TcpListener(address, redirectUri.Port);
		listener.Start();
		var pageOptions = _optionsFactory.Create(Options.DefaultName);
		var completedPage = string.Format(CultureInfo.InvariantCulture, CompletedPageFormat,
			WebUtility.HtmlEncode(pageOptions.Title), WebUtility.HtmlEncode(pageOptions.Message));

		await _externalBrowser.Open(new Uri(options.StartUrl));

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(options.Timeout);
		try
		{
			var response = await ReceiveRedirect(listener, redirectUri, completedPage, timeoutCts.Token);
			return new BrowserResult { ResultType = BrowserResultType.Success, Response = response };
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return new BrowserResult
			{
				ResultType = BrowserResultType.Timeout,
				Error = $"No authorization response within {options.Timeout}",
			};
		}
	}

	private async Task<string> ReceiveRedirect(TcpListener listener, Uri redirectUri, string completedPage, CancellationToken cancellationToken)
	{
		while (true)
		{
			using var client = await listener.AcceptTcpClientAsync(cancellationToken);
			var stream = client.GetStream();
			using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			requestCts.CancelAfter(RequestReadTimeout);
			try
			{
				var target = await ReadRequestTarget(stream, requestCts.Token);
				if (target is null)
				{
					await WriteResponse(stream, "400 Bad Request", string.Empty, requestCts.Token);
					continue;
				}

				if (!Uri.TryCreate(redirectUri, target, out var requestUri) ||
				    !string.Equals(requestUri.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal))
				{
					await WriteResponse(stream, "404 Not Found", string.Empty, requestCts.Token);
					continue;
				}

				await TryWriteCompletedPage(stream, completedPage, requestCts.Token, cancellationToken);
				return requestUri.AbsoluteUri;
			}
			catch (Exception e) when (e is IOException or OperationCanceledException &&
			                          !cancellationToken.IsCancellationRequested)
			{
				_logger.LogWarning(e, "Failed to handle a request to the OAuth redirect listener");
			}
		}
	}

	/// <summary>
	/// The authorization response is already received, so a browser that stops listening for the page must not
	/// lose it.
	/// </summary>
	private async Task TryWriteCompletedPage(Stream stream, string completedPage, CancellationToken requestToken, CancellationToken cancellationToken)
	{
		try
		{
			await WriteResponse(stream, "200 OK", completedPage, requestToken);
		}
		catch (Exception e) when (e is IOException or OperationCanceledException &&
		                          !cancellationToken.IsCancellationRequested)
		{
			_logger.LogWarning(e, "Failed to send the completion page of the OAuth redirect");
		}
	}

	/// <summary>
	/// Reads the request head and returns the target of a <c>GET</c> request, or <see langword="null"/> for any
	/// other or malformed request.
	/// </summary>
	private static async Task<string?> ReadRequestTarget(Stream stream, CancellationToken cancellationToken)
	{
		var buffer = new byte[MaxRequestHeadLength];
		var length = 0;
		int headEnd;
		while ((headEnd = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8)) < 0)
		{
			if (length == buffer.Length)
			{
				return null;
			}

			var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
			if (read == 0)
			{
				return null;
			}

			length += read;
		}

		var head = buffer.AsSpan(0, headEnd);
		var requestLineEnd = head.IndexOf("\r\n"u8);
		var requestLine = Encoding.ASCII.GetString(requestLineEnd < 0 ? head : head[..requestLineEnd]);
		return requestLine.Split(' ') is ["GET", var target, _] && target.StartsWith('/', StringComparison.Ordinal) ? target : null;
	}

	private static async Task WriteResponse(Stream stream, string status, string body, CancellationToken cancellationToken)
	{
		var bodyBytes = Encoding.UTF8.GetBytes(body);
		var head = string.Create(
			CultureInfo.InvariantCulture,
			$"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
		await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);
		await stream.WriteAsync(bodyBytes, cancellationToken);
	}
}
