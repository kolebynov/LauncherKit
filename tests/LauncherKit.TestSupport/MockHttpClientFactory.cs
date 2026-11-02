using RichardSzalay.MockHttp;

namespace LauncherKit.TestSupport;

public static class MockHttpClientFactory
{
	/// <summary>
	/// Registers an <see cref="IHttpClientFactory"/> mock on <paramref name="mocker"/> serving a single
	/// named client backed by <paramref name="handler"/>. Like the real factory's clients, disposing a client
	/// leaves the handler usable for the next one.
	/// </summary>
	public static IHttpClientFactory Factory(AutoMocker mocker, string name, MockHttpMessageHandler handler, Uri? baseAddress = null)
	{
		ArgumentNullException.ThrowIfNull(mocker);
		var mock = mocker.GetMock<IHttpClientFactory>();
		mock.Setup(x => x.CreateClient(name)).Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = baseAddress });
		return mock.Object;
	}

	/// <summary>
	/// Handler whose unmatched requests fail the test instead of returning a swallowable 404.
	/// </summary>
	public static MockHttpMessageHandler StrictHandler()
	{
		var handler = new MockHttpMessageHandler();
		handler.Fallback.Throw(new InvalidOperationException("Unexpected HTTP request"));
		return handler;
	}
}
