using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Services;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class ProcessRunnerTests
{
	[Test]
	public async Task StartProcess_ProcessExitsWithThree_ReturnsThree()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<ProcessRunner>>(NullLogger<ProcessRunner>.Instance);
		var sut = mocker.CreateInstance<ProcessRunner>();
		var startInfo = CreateStartInfo("exit 3");

		// Act
		var result = await sut.StartProcess(startInfo, CancellationToken.None);

		// Assert
		Assert.That(result, Is.EqualTo(3));
	}

	[Test]
	public void StartProcessAndAssertSuccess_NonZeroAndAssertEnabled_ThrowsWithExitCode()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<ProcessRunner>>(NullLogger<ProcessRunner>.Instance);
		var sut = mocker.CreateInstance<ProcessRunner>();
		var startInfo = CreateStartInfo("exit 3");

		// Act
		// Assert
		Assert.That(async () => await sut.StartProcessAndAssertSuccess(startInfo, true, CancellationToken.None),
			Throws.InvalidOperationException.With.Message.Contains("code 3"));
	}

	[Test]
	public void StartProcessAndAssertSuccess_NonZeroAndAssertDisabled_CompletesNormally()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<ProcessRunner>>(NullLogger<ProcessRunner>.Instance);
		var sut = mocker.CreateInstance<ProcessRunner>();
		var startInfo = CreateStartInfo("exit 3");

		// Act
		// Assert
		Assert.That(async () => await sut.StartProcessAndAssertSuccess(startInfo, false, CancellationToken.None), Throws.Nothing);
	}

	[Test]
	public void StartProcess_CancellationRequested_KillsProcessAndThrowsOperationCanceledException()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<ProcessRunner>>(NullLogger<ProcessRunner>.Instance);
		var sut = mocker.CreateInstance<ProcessRunner>();
		var startInfo = CreateStartInfo("ping 127.0.0.1 -t");
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		// Act
		// Assert
		Assert.That(async () => await sut.StartProcess(startInfo, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
	}

	private static ProcessStartInfo CreateStartInfo(string command) => new("cmd.exe", $"/d /c {command}")
	{
		UseShellExecute = false,
		CreateNoWindow = true,
	};
}
