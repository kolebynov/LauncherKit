using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.Core.Analytics;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Analytics;

[TestFixture]
public sealed class InstallationAnalyticsInitializerTests
{
	[Test]
	public async Task Initialize_ServiceFails_ReturnsImmediatelyAndContainsFailure()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallationAnalyticsInitializer>>(NullLogger<InstallationAnalyticsInitializer>.Instance);
		mocker.Use<TimeProvider>(TimeProvider.System);
		var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		mocker.GetMock<IInstallationAnalyticsService>()
			.Setup(x => x.ReportInstallation(It.IsAny<CancellationToken>()))
			.Returns(() =>
			{
				called.SetResult();
				return Task.FromException(new HttpRequestException("offline"));
			});
		var sut = mocker.CreateInstance<InstallationAnalyticsInitializer>(enablePrivate: true);

		// Act
		await sut.Initialize();

		// Assert
		await TaskAssert.WithinTimeout(called.Task);
	}

	[Test]
	public async Task Initialize_AfterFiveSeconds_CancelsReportingWithoutBlockingStartup()
	{
		// Arrange
		var time = new FakeTimeProvider();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallationAnalyticsInitializer>>(NullLogger<InstallationAnalyticsInitializer>.Instance);
		mocker.Use<TimeProvider>(time);
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		mocker.GetMock<IInstallationAnalyticsService>()
			.Setup(x => x.ReportInstallation(It.IsAny<CancellationToken>()))
			.Returns((CancellationToken token) => WaitForCancellation(cancelled, token));
		var sut = mocker.CreateInstance<InstallationAnalyticsInitializer>(enablePrivate: true);

		// Act
		await sut.Initialize();
		time.Advance(TimeSpan.FromSeconds(5));

		// Assert
		await TaskAssert.WithinTimeout(cancelled.Task);
	}

	private static async Task WaitForCancellation(TaskCompletionSource cancelled, CancellationToken token)
	{
		try
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, token);
		}
		catch (OperationCanceledException)
		{
			cancelled.TrySetResult();
			throw;
		}
	}
}
