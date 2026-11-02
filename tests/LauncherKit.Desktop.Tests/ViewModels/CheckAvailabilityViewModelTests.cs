using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class CheckAvailabilityViewModelTests
{
	[Test]
	public void FinishCommand_BeforeCheckCompletes_CannotExecute()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var check = new TaskCompletionSource<bool>();
		mocker.GetMock<IFeedAvailabilityChecker>().Setup(x => x.CheckFeed()).Returns(check.Task);
		var sut = new CheckAvailabilityViewModel(
			mocker.GetMock<IFeedAvailabilityChecker>().Object, _ => Task.CompletedTask);

		// Act
		var execution = sut.CheckAvailabilityCommand.ExecuteAsync(null);

		// Assert
		Assert.That(sut.FinishCommand.CanExecute(null), Is.False);
		check.SetResult(true);
		Assert.That(async () => await execution, Throws.Nothing);
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task CheckAvailability_CheckResult_AllowsFinishingWhetherOrNotAServerWorks(bool isAvailable)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedAvailabilityChecker>().Setup(x => x.CheckFeed()).ReturnsAsync(isAvailable);
		var sut = new CheckAvailabilityViewModel(
			mocker.GetMock<IFeedAvailabilityChecker>().Object, _ => Task.CompletedTask);

		// Act
		await sut.CheckAvailabilityCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsAvailable, Is.EqualTo(isAvailable));
			Assert.That(sut.IsUnavailable, Is.EqualTo(!isAvailable));
			Assert.That(sut.FinishCommand.CanExecute(null), Is.True);
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task Finish_AfterCheck_FinishesOnce(bool isAvailable)
	{
		// Arrange
		var finishes = 0;
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedAvailabilityChecker>().Setup(x => x.CheckFeed()).ReturnsAsync(isAvailable);
		var sut = new CheckAvailabilityViewModel(
			mocker.GetMock<IFeedAvailabilityChecker>().Object,
			_ =>
			{
				finishes++;
				return Task.CompletedTask;
			});
		await sut.CheckAvailabilityCommand.ExecuteAsync(null);

		// Act
		await sut.FinishCommand.ExecuteAsync(null);

		// Assert
		Assert.That(finishes, Is.EqualTo(1));
	}
}
