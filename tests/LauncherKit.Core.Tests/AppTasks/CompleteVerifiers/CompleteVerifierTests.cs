using R3;
using LauncherKit.Core.AppTasks.CompleteVerifiers;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.AppTasks.CompleteVerifiers;

[TestFixture]
public sealed class FileExistsVerifierTests
{
	[TestCase(false, true)]
	[TestCase(true, false)]
	public async Task IsTaskComplete_FileExists_AppliesInvertFlag(bool invert, bool expected)
	{
		// Arrange
		using var temp = new TempDirectory();
		var path = temp.CreateFile("complete.marker");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(path);
		mocker.Use(invert);
		var sut = mocker.CreateInstance<FileExistsVerifier>(enablePrivate: true);

		// Act
		var result = await sut.IsTaskComplete(CancellationToken.None);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public async Task IsTaskComplete_FileMissing_ReturnsFalse()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(temp.Combine("missing"));
		mocker.Use(false);
		var sut = mocker.CreateInstance<FileExistsVerifier>(enablePrivate: true);

		// Act
		var result = await sut.IsTaskComplete(CancellationToken.None);

		// Assert
		Assert.That(result, Is.False);
	}
}

[TestFixture]
public sealed class DelegateCompleteVerifierTests
{
	[Test]
	public async Task IsTaskComplete_Delegate_PropagatesTokenAndResult()
	{
		// Arrange
		using var cancellation = new CancellationTokenSource();
		var received = CancellationToken.None;
		Func<CancellationToken, ValueTask<bool>> verifier = token =>
		{
			received = token;
			return ValueTask.FromResult(true);
		};
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(verifier);
		var sut = mocker.CreateInstance<DelegateCompleteVerifier>(enablePrivate: true);

		// Act
		var result = await sut.IsTaskComplete(cancellation.Token);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.True);
			Assert.That(received, Is.EqualTo(cancellation.Token));
		});
	}
}

[TestFixture]
public sealed class WithCompleteVerificationMicroTaskTests
{
	[TestCase(true, 0)]
	[TestCase(false, 1)]
	public async Task Execute_VerifierResult_ControlsInnerExecution(bool alreadyComplete, int expectedExecutions)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IAppTaskCompleteVerifier>()
			.Setup(x => x.IsTaskComplete(CancellationToken.None))
			.ReturnsAsync(alreadyComplete);
		if (!alreadyComplete)
		{
			mocker.GetMock<IAppMicroTask>()
				.Setup(x => x.Execute(It.IsAny<ISubject<MicroTaskStep>>(), CancellationToken.None))
				.Returns(ValueTask.CompletedTask);
		}
		var sut = mocker.CreateInstance<WithCompleteVerificationMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		mocker.GetMock<IAppMicroTask>().Verify(
			x => x.Execute(It.IsAny<ISubject<MicroTaskStep>>(), CancellationToken.None),
			Times.Exactly(expectedExecutions));
	}
}
