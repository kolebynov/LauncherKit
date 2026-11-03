using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class ActivateBetaCodeViewModelTests
{
	[Test]
	public async Task ActivateBetaCode_EmptyCode_ShowsValidationErrorWithoutCallingProvider()
	{
		// Arrange
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<ActivateBetaCodeViewModel>();

		// Act
		await sut.ActivateBetaCodeCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Result is ActivateBetaCodeResult.InvalidBetaCode, Is.True);
			Assert.That(sut.IsActivated, Is.False);
		});
	}

	[Test]
	public async Task ActivateBetaCode_ValidCode_MarksActivated()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.ActivateBetaCode("valid", CancellationToken.None))
			.ReturnsAsync(new ActivateBetaCodeResult.Success());
		var sut = mocker.CreateInstance<ActivateBetaCodeViewModel>();
		sut.BetaCode = "valid";

		// Act
		await sut.ActivateBetaCodeCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Result is ActivateBetaCodeResult.Success, Is.True);
			Assert.That(sut.IsActivated, Is.True);
		});
	}

	[Test]
	public async Task ActivateBetaCode_InvalidCode_ShowsErrorAndRemainsInactive()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.ActivateBetaCode("invalid", CancellationToken.None))
			.ReturnsAsync(new ActivateBetaCodeResult.InvalidBetaCode());
		var sut = mocker.CreateInstance<ActivateBetaCodeViewModel>();
		sut.BetaCode = "invalid";

		// Act
		await sut.ActivateBetaCodeCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Result is ActivateBetaCodeResult.InvalidBetaCode, Is.True);
			Assert.That(sut.IsActivated, Is.False);
		});
	}
}
