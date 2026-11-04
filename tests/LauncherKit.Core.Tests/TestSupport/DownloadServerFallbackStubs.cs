using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.TestSupport;

internal static class DownloadServerFallbackStubs
{
	/// <summary>
	/// Configures the fallback to run operations returning <typeparamref name="T"/> once, with <paramref name="baseUri"/>.
	/// </summary>
	public static void SingleServer<T>(AutoMocker mocker, Uri baseUri) =>
		mocker.GetMock<IDownloadServerFallback>()
			.Setup(x => x.Execute(
				It.IsAny<string>(), It.IsAny<Func<Uri, CancellationToken, Task<T>>>(), It.IsAny<CancellationToken>()))
			.Returns((string _, Func<Uri, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
				operation(baseUri, cancellationToken));
}
