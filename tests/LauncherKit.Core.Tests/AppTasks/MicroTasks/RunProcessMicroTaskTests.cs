using System.Diagnostics;
using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class RunProcessMicroTaskTests
{
	[TestCase(false, false)]
	[TestCase(true, true)]
	public void Execute_NonZeroExit_HonorsAssertSuccessFlag(bool assertSuccess, bool throws)
	{
		// Arrange
		var startInfo = new ProcessStartInfo("tool.exe", "--check");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<MicroTaskDescription>(new MicroTaskDescription.RunProcess("tool.exe"));
		mocker.Use<Func<ProcessStartInfo>>(() => startInfo);
		mocker.Use(assertSuccess);
		mocker.GetMock<IProcessRunner>()
			.Setup(x => x.StartProcess(startInfo, CancellationToken.None, null))
			.ReturnsAsync(3);
		var sut = mocker.CreateInstance<RunProcessMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		var execution = sut.Execute(subject, CancellationToken.None).AsTask();

		// Assert
		Assert.That(async () => await execution, throws
			? Throws.InvalidOperationException.With.Message.Contains("code 3")
			: Throws.Nothing);
	}
}
