using CommunityToolkit.Mvvm.Input;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.Localization;
using LauncherKit.Core.Models;

namespace LauncherKit.Desktop.Avalonia.Tests.ViewModels;

[TestFixture]
public sealed class ModCardViewModelTests
{
	[Test]
	public async Task InstallCommand_AvailableMod_ExecutesInstallAndRefreshesInstalledState()
	{
		var state = ModInstallState.NotInstalled;
		var innerCommand = new AsyncRelayCommand(() =>
		{
			state = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true);
			return Task.CompletedTask;
		});
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => state, innerCommand, new AsyncRelayCommand(() => Task.CompletedTask));

		await sut.InstallCommand.ExecuteAsync(null);

		Assert.Multiple(() =>
		{
			Assert.That(sut.IsUninstallSupported, Is.True);
			Assert.That(sut.IsInstalled, Is.True);
			Assert.That(sut.IsUpdateAvailable, Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public async Task InstallCommand_UpdateAvailable_ExecutesUpdateAndClearsUpdateState()
	{
		var state = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: false);
		var innerCommand = new AsyncRelayCommand(() =>
		{
			state = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: false);
			return Task.CompletedTask;
		});
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => state, innerCommand, new AsyncRelayCommand(() => Task.CompletedTask));
		var canUpdate = sut.InstallCommand.CanExecute(null);

		await sut.InstallCommand.ExecuteAsync(null);

		Assert.Multiple(() =>
		{
			Assert.That(canUpdate, Is.True);
			Assert.That(sut.IsInstalled, Is.True);
			Assert.That(sut.IsUpdateAvailable, Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public void UnavailableReason_InstallCommandBecomesUnavailableAndAvailable_FollowsCommandState()
	{
		var otherGraphPack = (string?)null;
		var innerCommand = new AsyncRelayCommand(() => Task.CompletedTask, () => otherGraphPack == null);
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => ModInstallState.NotInstalled, innerCommand, new AsyncRelayCommand(() => Task.CompletedTask),
			() => new ModCardViewModel.InstallUnavailableReason.OtherGraphPackInstalled(otherGraphPack!));
		var initialReason = sut.UnavailableReason;

		otherGraphPack = "Other";
		innerCommand.NotifyCanExecuteChanged();
		var blockedReason = sut.UnavailableReason;
		var blockedCanInstall = sut.InstallCommand.CanExecute(null);
		otherGraphPack = null;
		innerCommand.NotifyCanExecuteChanged();

		Assert.Multiple(() =>
		{
			Assert.That(initialReason, Is.Null);
			Assert.That(blockedReason, Is.EqualTo(new ModCardViewModel.InstallUnavailableReason.OtherGraphPackInstalled("Other")));
			Assert.That(blockedCanInstall, Is.False);
			Assert.That(sut.UnavailableReason, Is.Null);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public void UnavailableReason_UnavailableWithoutReasonFunc_IsUpdatingReason()
	{
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => ModInstallState.NotInstalled, new AsyncRelayCommand(() => Task.CompletedTask, () => false),
			new AsyncRelayCommand(() => Task.CompletedTask));

		Assert.That(sut.UnavailableReason, Is.TypeOf<ModCardViewModel.InstallUnavailableReason.Updating>());
	}

	[Test]
	public async Task UninstallCommand_InstalledMod_ExecutesUninstallAndAllowsInstallAgain()
	{
		var state = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true);
		var innerCommand = new AsyncRelayCommand(() =>
		{
			state = ModInstallState.NotInstalled;
			return Task.CompletedTask;
		});
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => state, new AsyncRelayCommand(() => Task.CompletedTask), innerCommand);
		var canUninstall = sut.UninstallCommand.CanExecute(null);

		await sut.UninstallCommand.ExecuteAsync(null);

		Assert.Multiple(() =>
		{
			Assert.That(canUninstall, Is.True);
			Assert.That(sut.IsInstalled, Is.False);
			Assert.That(sut.IsUpdateAvailable, Is.False);
			Assert.That(sut.UninstallCommand.CanExecute(null), Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public async Task UninstallCommand_InstalledMod_NotifiesInstallCommandItCanExecuteAgain()
	{
		var state = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true);
		var innerCommand = new AsyncRelayCommand(() =>
		{
			state = ModInstallState.NotInstalled;
			return Task.CompletedTask;
		});
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => state, new AsyncRelayCommand(() => Task.CompletedTask), innerCommand);
		bool? notifiedCanInstall = null;
		// A bound button re-queries CanExecute only when CanExecuteChanged is raised.
		sut.InstallCommand.CanExecuteChanged += (_, _) => notifiedCanInstall = sut.InstallCommand.CanExecute(null);

		await sut.UninstallCommand.ExecuteAsync(null);

		Assert.That(notifiedCanInstall, Is.True);
	}

	[Test]
	public void UninstallCommand_InstalledVersionWithoutUninstallSupport_CannotExecute()
	{
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: false),
			new AsyncRelayCommand(() => Task.CompletedTask), new AsyncRelayCommand(() => Task.CompletedTask));

		Assert.Multiple(() =>
		{
			Assert.That(sut.IsUninstallSupported, Is.False);
			Assert.That(sut.UninstallCommand.CanExecute(null), Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public async Task UninstallCommand_UpdateRunning_CannotExecuteUntilUpdateCompletes()
	{
		var update = new TaskCompletionSource();
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), new Uri("https://assets.example.test/readme.md"),
			() => ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true),
			new AsyncRelayCommand(() => update.Task),
			new AsyncRelayCommand(() => Task.CompletedTask));

		var updateTask = sut.InstallCommand.ExecuteAsync(null);
		var canUninstallDuringUpdate = sut.UninstallCommand.CanExecute(null);
		update.SetResult();
		await updateTask;

		Assert.Multiple(() =>
		{
			Assert.That(canUninstallDuringUpdate, Is.False);
			Assert.That(sut.UninstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public void DescriptionUri_AndActionFacts_AreExposedWithoutUiRequests()
	{
		var description = new Uri("https://assets.example.test/readme.md");
		var state = ModInstallState.NotInstalled;
		var sut = new ModCardViewModel(
			"Example", new Uri("https://assets.example.test/logo.png"), description, () => state,
			new AsyncRelayCommand(() => Task.CompletedTask), new AsyncRelayCommand(() => Task.CompletedTask),
			installRequiresNewGame: true, uninstallRequiresNewGame: true);
		Assert.Multiple(() =>
		{
			Assert.That(sut.DescriptionUri, Is.EqualTo(description));
			Assert.That(sut.InstallRequiresNewGame, Is.True);
			Assert.That(sut.UpdateRequiresNewGame, Is.False);
			Assert.That(sut.UninstallRequiresNewGame, Is.True);
		});
	}

	[Test]
	public void UnavailableFactTexts_MapTypedCasesToDesktopResourceKeysAndRawName()
	{
		var strings = new Mock<IStringLocalizer<DesktopStrings>>(MockBehavior.Strict);
		strings.Setup(x => x[It.IsAny<string>()]).Returns<string>(key => new LocalizedString(key, key));
		strings.Setup(x => x[DesktopStrings.OtherGraphPackInstalled, It.Is<object[]>(args => args.Length == 1 && Equals(args[0], "Other"))])
			.Returns(new LocalizedString(DesktopStrings.OtherGraphPackInstalled, DesktopStrings.OtherGraphPackInstalled));
		var texts = new HomeCardFactTexts(strings.Object);
		Assert.Multiple(() =>
		{
			Assert.That(texts.Get(ModpackInstallUnavailableReason.NoAvailableVersions).Name, Is.EqualTo(DesktopStrings.ModpackInstallUnavailable));
			Assert.That(texts.Get(new ModCardViewModel.InstallUnavailableReason.Updating()).Name, Is.EqualTo(DesktopStrings.ModUpdating));
			Assert.That(texts.Get(new ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued()).Name, Is.EqualTo(DesktopStrings.ModpackTaskQueued));
			Assert.That(texts.Get(new ModCardViewModel.InstallUnavailableReason.OtherGraphPackInstalled("Other")).Name, Is.EqualTo(DesktopStrings.OtherGraphPackInstalled));
		});
		strings.Verify(x => x[DesktopStrings.OtherGraphPackInstalled, It.IsAny<object[]>()], Times.Once);
	}
}
