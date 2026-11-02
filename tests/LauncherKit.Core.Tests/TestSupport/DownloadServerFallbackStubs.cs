using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.TestSupport;

internal static class DownloadServerFallbackStubs
{
	/// <summary>Configures the fallback to run operations returning <typeparamref name="T"/> once, on <paramref name="server"/>.</summary>
	public static void SingleServer<T>(AutoMocker mocker, DownloadServer server = DownloadServer.Main) =>
		mocker.GetMock<IDownloadServerFallback>()
			.Setup(x => x.Execute(
				It.IsAny<Func<DownloadServer, CancellationToken, Task<T>>>(), It.IsAny<CancellationToken>()))
			.Returns((Func<DownloadServer, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
				operation(server, cancellationToken));
}
