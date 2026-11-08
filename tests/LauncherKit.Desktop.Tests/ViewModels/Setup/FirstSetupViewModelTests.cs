using System.Globalization;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Core.Utils;
using LauncherKit.TestSupport;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Tests.ViewModels.Setup;

[TestFixture]
public sealed class FirstSetupViewModelTests
{
	[Test]
	public async Task Start_FirstCall_ShowsFirstStepWithoutBackAndSecondCallIsIdempotent()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings());
		var first = new TestStep();
		var second = new TestStep();
		var sut = CreateSut(mocker, first, second);

		// Act
		await sut.Start();
		await sut.Start();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.CurrentStep, Is.SameAs(first));
			Assert.That(sut.State, Is.EqualTo(FirstSetupState.InProgress));
			Assert.That(first.IsShown, Is.True);
			Assert.That(first.CanGoBack, Is.False);
			Assert.That(second.IsShown, Is.False);
		});
	}

	[Test]
	public async Task Back_FromSecondStep_ShowsFirstStepAndHidesSecond()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings());
		var first = new TestStep();
		var second = new TestStep();
		var sut = CreateSut(mocker, first, second);
		await sut.Start();
		await first.Next();
		Assert.That(second.BackCommand.CanExecute(null), Is.True);

		// Act
		await second.BackCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.CurrentStep, Is.SameAs(first));
			Assert.That(second.IsShown, Is.False);
			Assert.That(second.BackCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public async Task GoNext_HiddenStep_DoesNotMoveSetup()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings());
		var first = new TestStep();
		var second = new TestStep();
		var sut = CreateSut(mocker, first, second);
		await sut.Start();
		await first.Next();

		// Act
		await first.Next();
		await first.Back();

		// Assert
		Assert.That(sut.CurrentStep, Is.SameAs(second));
		Assert.That(sut.State, Is.EqualTo(FirstSetupState.InProgress));
	}

	[Test]
	public async Task GoNext_LastStep_SavesEditedSettingsAndFinishes()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings());
		var step = new TestStep();
		var context = new FirstSetupContext(mocker.Get<ISettingsService>().GetSettings());
		mocker.GetMock<ISettingsService>().Setup(x => x.SaveSettings(context.Settings)).Returns(Task.CompletedTask);
		var sut = new FirstSetupViewModel([step], context, mocker.Get<ISettingsService>());
		await sut.Start();

		// Act
		await step.Next();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.State, Is.EqualTo(FirstSetupState.Finished));
			Assert.That(step.IsShown, Is.False);
		});
		mocker.GetMock<ISettingsService>().Verify(x => x.SaveSettings(context.Settings), Times.Once);
	}

	[Test]
	public void Create_ProvidersOutOfOrder_ConcatenatesStepsByOrderKeepingRegistrationOrderForTies()
	{
		// Arrange
		var context = new FirstSetupContext(CreateSettings(new AppSettings()));
		var late = new TestStep();
		var tieFirst = new TestStep();
		var tieSecond = new TestStep();
		var early = new TestStep();

		// Act
		var steps = FirstSetupSteps.Create(
			[new TestProvider(10, late), new TestProvider(5, tieFirst), new TestProvider(5, tieSecond), new TestProvider(1, early)],
			context);

		// Assert
		Assert.That(steps, Is.EqualTo(new[] { early, tieFirst, tieSecond, late }));
	}

	[Test]
	public void KitProviders_DirectoriesSetAndCheckSucceeded_CreateNoSteps()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings { SkyrimDirectory = "Skyrim", AppDataDirectory = "Data" });
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.HasKey(AvailabilityCheckState.CheckSucceededKey)).Returns(true);

		// Act
		var steps = CreateKitSteps(mocker, out _);

		// Assert
		Assert.That(steps, Is.Empty);
	}

	[Test]
	public void KitProviders_DirectoriesSetWithoutSucceededCheck_CreateOnlyAvailabilityStep()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings { SkyrimDirectory = "Skyrim", AppDataDirectory = "Data" });

		// Act
		var steps = CreateKitSteps(mocker, out _);

		// Assert
		Assert.That(steps.Select(x => x.GetType()), Is.EqualTo(new[] { typeof(AvailabilitySetupStepViewModel) }));
	}

	[Test]
	public void KitProviders_NothingSet_CreateSkyrimDataAndAvailabilitySteps()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings());

		// Act
		var steps = CreateKitSteps(mocker, out _);

		// Assert
		Assert.That(steps.Select(x => x.GetType()), Is.EqualTo(new[]
		{
			typeof(SkyrimDirectorySetupStepViewModel), typeof(AppDataDirectorySetupStepViewModel),
			typeof(AvailabilitySetupStepViewModel),
		}));
	}

	[TestCase(true, 1)]
	[TestCase(false, 0)]
	public async Task AvailabilityStep_CheckFinished_StoresKeyOnlyAfterSuccess(bool isAvailable, int stored)
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings());
		mocker.GetMock<IFeedAvailabilityChecker>().Setup(x => x.CheckFeed()).ReturnsAsync(isAvailable);
		var sut = new AvailabilitySetupStepViewModel(mocker.Get<IFeedAvailabilityChecker>(), mocker.Get<AvailabilityCheckState>());

		// Act
		await sut.Availability.CheckAvailabilityCommand.ExecuteAsync(null);

		// Assert
		mocker.GetMock<IGlobalKeyValueStorage>().Verify(x => x.Set(AvailabilityCheckState.CheckSucceededKey, "1"), Times.Exactly(stored));
	}

	[Test]
	public async Task AvailabilityStep_ContinueWithoutDownloads_SkipsCheckOnlyUntilNextStart()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings { SkyrimDirectory = "Skyrim", AppDataDirectory = "Data" });
		mocker.GetMock<IFeedAvailabilityChecker>().Setup(x => x.CheckFeed()).ReturnsAsync(false);
		mocker.GetMock<ISettingsService>().Setup(x => x.SaveSettings(It.IsAny<AllSettings>())).Returns(Task.CompletedTask);
		var steps = CreateKitSteps(mocker, out var context);
		var sut = CreateSut(mocker, context, steps);
		await sut.Start();
		var availability = ((AvailabilitySetupStepViewModel)sut.CurrentStep!).Availability;
		await availability.CheckAvailabilityCommand.ExecuteAsync(null);

		// Act
		await availability.FinishCommand.ExecuteAsync(null);

		// Assert: the setup's reload creates no steps, a new run's state requires the check again.
		Assert.Multiple(() =>
		{
			Assert.That(sut.State, Is.EqualTo(FirstSetupState.Finished));
			Assert.That(CreateKitSteps(mocker, out _), Is.Empty);
			Assert.That(new AvailabilityCheckState(mocker.Get<IGlobalKeyValueStorage>()).IsRequired, Is.True);
		});
	}

	[Test]
	public async Task AppDataDirectoryStep_NestedInChosenSkyrimDirectory_BlocksNext()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		var mocker = CreateMocker(new AppSettings());
		mocker.GetMock<ISkyrimInstallationChecker>().Setup(x => x.CheckSkyrimDirectory(skyrim.Path))
			.Returns(SkyrimDirectoryCheckResult.NotSkyrimDirectory.Instance);
		var steps = CreateKitSteps(mocker, out var context);
		var sut = CreateSut(mocker, context, steps);
		await sut.Start();
		var skyrimStep = (SkyrimDirectorySetupStepViewModel)steps[0];
		skyrimStep.Directory.SelectedDirectory = skyrim.Path;
		await skyrimStep.NextCommand.ExecuteAsync(null);
		var dataStep = (AppDataDirectorySetupStepViewModel)sut.CurrentStep!;

		// Act
		dataStep.Directory.SelectedDirectory = skyrim.Combine("LauncherData");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(context.Settings.App.SkyrimDirectory, Is.EqualTo(skyrim.Path));
			Assert.That(dataStep.Directory.GetErrors(null), Is.EqualTo(new[] { DirectoryValidationError.AppDataDirectoryConflict }));
			Assert.That(dataStep.NextCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public async Task SkyrimStep_UnsupportedVersion_ExposesErrorAndStays()
	{
		// Arrange
		using var directory = new TempDirectory();
		var detected = new Version(0, 1);
		var mocker = CreateMocker(new AppSettings());
		mocker.GetMock<ISkyrimInstallationChecker>().Setup(x => x.CheckSkyrimDirectory(directory.Path))
			.Returns(new SkyrimDirectoryCheckResult.SkyrimDirectory(detected));
		var steps = CreateKitSteps(mocker, out var context);
		var sut = CreateSut(mocker, context, steps);
		await sut.Start();
		var skyrimStep = (SkyrimDirectorySetupStepViewModel)sut.CurrentStep!;
		skyrimStep.Directory.SelectedDirectory = directory.Path;

		// Act
		await skyrimStep.NextCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.CurrentStep, Is.SameAs(skyrimStep));
			Assert.That(skyrimStep.Error!.DetectedSkyrimVersion, Is.EqualTo(detected));
			Assert.That(context.Settings.App.SkyrimDirectory, Is.Null);
		});
	}

	[Test]
	public async Task SkyrimStep_FirstValidDirectory_NotifiesNextAvailabilityWithoutValidationErrorTransition()
	{
		// Arrange
		using var directory = new TempDirectory();
		var mocker = CreateMocker(new AppSettings());
		var steps = CreateKitSteps(mocker, out var context);
		var sut = CreateSut(mocker, context, steps);
		await sut.Start();
		var skyrimStep = (SkyrimDirectorySetupStepViewModel)sut.CurrentStep!;
		Assert.That(skyrimStep.NextCommand.CanExecute(null), Is.False);
		var notified = false;
		skyrimStep.NextCommand.CanExecuteChanged += (_, _) => notified = true;

		// Act
		skyrimStep.Directory.SelectedDirectory = directory.Path;

		// Assert
		Assert.That(notified, Is.True);
		Assert.That(skyrimStep.NextCommand.CanExecute(null), Is.True);
	}

	[Test]
	public async Task AvailabilityStep_Finish_CompletesSetup()
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings { SkyrimDirectory = "Skyrim" });
		mocker.GetMock<IFeedAvailabilityChecker>().Setup(x => x.CheckFeed()).ReturnsAsync(false);
		mocker.GetMock<ISettingsService>().Setup(x => x.SaveSettings(It.IsAny<AllSettings>())).Returns(Task.CompletedTask);
		var steps = CreateKitSteps(mocker, out var context);
		var sut = CreateSut(mocker, context, steps);
		await sut.Start();
		await ((AppDataDirectorySetupStepViewModel)sut.CurrentStep!).NextCommand.ExecuteAsync(null);
		var availability = ((AvailabilitySetupStepViewModel)sut.CurrentStep!).Availability;
		await availability.CheckAvailabilityCommand.ExecuteAsync(null);

		// Act
		await availability.FinishCommand.ExecuteAsync(null);

		// Assert
		Assert.That(sut.State, Is.EqualTo(FirstSetupState.Finished));
	}

	[TestCase(null, true)]
	[TestCase("", true)]
	[TestCase("de", true)]
	[TestCase("en", false)]
	[TestCase("ru", false)]
	public void LanguageProvider_StoredLanguage_OffersStepUntilSupportedLanguageChosen(string? language, bool offered)
	{
		// Arrange
		var mocker = CreateMocker(new AppSettings { Language = language });
		var context = new FirstSetupContext(mocker.Get<ISettingsService>().GetSettings());

		// Act
		var steps = mocker.CreateInstance<LanguageSetupStepProvider>().CreateSteps(context).ToArray();

		// Assert
		Assert.That(steps.Length == 1 && steps[0] is LanguageSetupStepViewModel, Is.EqualTo(offered));
	}

	[TestCase("ru-RU", "ru")]
	[TestCase("en-GB", "en")]
	[TestCase("de-DE", "en")]
	public void LanguageStep_OperatingSystemCulture_PreselectsItWhenSupportedOtherwiseEnglish(string culture, string expected)
	{
		// Arrange
		var previous = CultureInfo.CurrentUICulture;
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
		try
		{
			var mocker = CreateMocker(new AppSettings());

			// Act
			var sut = new LanguageSetupStepViewModel(new FirstSetupContext(mocker.Get<ISettingsService>().GetSettings()),
				mocker.Get<IAppReloader>());

			// Assert
			Assert.That(sut.SelectedLanguage, Is.EqualTo(expected));
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	[Test]
	public async Task LanguageStep_Next_StoresAndAppliesLanguageBeforeNextStep()
	{
		// Arrange
		var previous = CultureInfo.DefaultThreadCurrentUICulture;
		try
		{
			var mocker = CreateMocker(new AppSettings());
			mocker.GetMock<IAppReloader>().Setup(x => x.ReloadViews());
			var context = new FirstSetupContext(mocker.Get<ISettingsService>().GetSettings());
			var step = new LanguageSetupStepViewModel(context, mocker.Get<IAppReloader>());
			var next = new TestStep();
			var sut = CreateSut(mocker, context, [step, next]);
			await sut.Start();
			step.SelectedLanguage = "ru";

			// Act
			await step.NextCommand.ExecuteAsync(null);

			// Assert
			Assert.Multiple(() =>
			{
				Assert.That(context.Settings.App.Language, Is.EqualTo("ru"));
				Assert.That(CultureInfo.DefaultThreadCurrentUICulture?.Name, Is.EqualTo("ru"));
				Assert.That(sut.CurrentStep, Is.SameAs(next));
			});
			mocker.GetMock<IAppReloader>().Verify(x => x.ReloadViews(), Times.Once);
		}
		finally
		{
			CultureInfo.DefaultThreadCurrentUICulture = previous;
		}
	}

	private static IReadOnlyList<SetupStepViewModel> CreateKitSteps(AutoMocker mocker, out FirstSetupContext context)
	{
		context = new FirstSetupContext(mocker.Get<ISettingsService>().GetSettings());
		return FirstSetupSteps.Create(
		[
			mocker.CreateInstance<AvailabilitySetupStepProvider>(),
			mocker.CreateInstance<AppDataDirectorySetupStepProvider>(),
			mocker.CreateInstance<SkyrimDirectorySetupStepProvider>(),
		], context);
	}

	private static FirstSetupViewModel CreateSut(AutoMocker mocker, params SetupStepViewModel[] steps) =>
		CreateSut(mocker, new FirstSetupContext(mocker.Get<ISettingsService>().GetSettings()), steps);

	private static FirstSetupViewModel CreateSut(AutoMocker mocker, FirstSetupContext context,
		IReadOnlyList<SetupStepViewModel> steps) =>
		new(steps, context, mocker.Get<ISettingsService>());

	private static AutoMocker CreateMocker(AppSettings app)
	{
		var mocker = ProductTestSupport.CreateMocker();
		mocker.Use<IUiDispatcher>(ImmediateUiDispatcher.Instance);
		mocker.GetMock<ISkyrimDirectoryFinder>().Setup(x => x.FindSkyrimDirectory()).Returns((string?)null);
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.HasKey(AvailabilityCheckState.CheckSucceededKey)).Returns(false);
		storage.Setup(x => x.Set(AvailabilityCheckState.CheckSucceededKey, "1"));
		mocker.Use(new AvailabilityCheckState(storage.Object));
		mocker.GetMock<ISettingsService>().Setup(x => x.GetSettings()).Returns(CreateSettings(app));
		return mocker;
	}

	private static AllSettings CreateSettings(AppSettings app) => new()
	{
		App = app, Download = new DownloadSettings(), Window = new WindowSettings(),
	};

	private sealed class TestStep : SetupStepViewModel
	{
		public Task Next() => GoNext();

		public Task Back() => GoBack();
	}

	private sealed class TestProvider(int order, SetupStepViewModel step) : ISetupStepProvider
	{
		public int Order => order;

		public IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context) => [step];
	}
}
