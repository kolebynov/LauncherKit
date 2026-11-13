using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Localization;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class ModCardViewModelTests
{
	private static readonly NuGetVersion ModpackVersion = NuGetVersion.Parse("3.2.0");

	[Test]
	public async Task InstallCommand_AvailableMod_InstallsSupportedVersionAndRefreshesInstalledState()
	{
		// Arrange
		using var context = new CardContext();
		var mod = Mod("Example");
		context.Installations.Setup(x => x.InstallMod(mod, It.IsAny<ModCategoryFeedData>(), NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
			.Callback(() => context.States[mod] = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true))
			.Returns(Task.CompletedTask);
		var sut = context.Card(mod);

		// Act
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsUninstallSupported, Is.True);
			Assert.That(sut.IsInstalled, Is.True);
			Assert.That(sut.IsUpdateAvailable, Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.False);
			Assert.That(sut.UninstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public async Task InstallCommand_UpdateAvailable_UpdatesAndClearsUpdateState()
	{
		// Arrange
		using var context = new CardContext();
		var mod = Mod("Example");
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true);
		context.Installations.Setup(x => x.InstallMod(mod, It.IsAny<ModCategoryFeedData>(), NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
			.Callback(() => context.States[mod] = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true))
			.Returns(Task.CompletedTask);
		var sut = context.Card(mod);
		var canUpdate = sut.InstallCommand.CanExecute(null);

		// Act
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(canUpdate, Is.True);
			Assert.That(sut.IsUpdateAvailable, Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public void UnavailableReason_NoVersionSupportsInstalledModpack_IsNotUpdated()
	{
		using var context = new CardContext();
		var sut = context.Card(Mod("Example", "[2.0.0, 3.0.0)"));

		Assert.Multiple(() =>
		{
			Assert.That(sut.IsAvailable, Is.False);
			Assert.That(sut.UnavailableReason, Is.EqualTo(ModCardViewModel.InstallUnavailableReason.NotUpdated.Instance));
			Assert.That(sut.InstallCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public void ModpackQueued_BlocksInstallAndUninstallUntilFinished_AndRefreshesInstalledFacts()
	{
		// Arrange
		using var context = new CardContext();
		var mod = Mod("Example");
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true);
		var sut = context.Card(mod);

		// Act
		context.Queue(QueuedInstallation.Modpack.Instance);
		var reasonWhileQueued = sut.UnavailableReason;
		var canUninstallWhileQueued = sut.UninstallCommand.CanExecute(null);
		// The chain reinstalled the modpack without the mod and without changing the modpack version.
		context.States[mod] = ModInstallState.NotInstalled;
		context.Finish(QueuedInstallation.Modpack.Instance);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(reasonWhileQueued, Is.EqualTo(ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued.Instance));
			Assert.That(canUninstallWhileQueued, Is.False);
			Assert.That(sut.IsAvailable, Is.True);
			Assert.That(sut.IsInstalled, Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public void ModQueuedFromAnotherCard_BlocksBothCommandsUntilFinished()
	{
		// Arrange
		using var context = new CardContext();
		var mod = Mod("Example");
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true);
		var sut = context.Card(mod);
		var installation = new QueuedInstallation.ModInstall(mod);

		// Act
		context.Queue(installation);
		var canInstallWhileQueued = sut.InstallCommand.CanExecute(null);
		var canUninstallWhileQueued = sut.UninstallCommand.CanExecute(null);
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true);
		context.Finish(installation);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(canInstallWhileQueued, Is.False);
			Assert.That(canUninstallWhileQueued, Is.False);
			Assert.That(sut.IsAvailable, Is.True);
			Assert.That(sut.IsUpdateAvailable, Is.False);
			Assert.That(sut.UninstallCommand.CanExecute(null), Is.True);
		});
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	public void OtherModOfCategoryInstalledOrQueued_BlocksOnlyInExclusiveCategory(bool isExclusive, bool expectedAvailable)
	{
		// Arrange
		using var context = new CardContext();
		var lite = Mod("Lite");
		var hidden = Mod("Old", "[2.0.0, 3.0.0)");
		var full = Mod("Full");
		context.States[hidden] = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true);
		var sut = context.Card(lite, isExclusive, [lite, hidden, full]);
		var installedReason = sut.UnavailableReason;
		context.States[hidden] = ModInstallState.NotInstalled;

		// Act
		context.Queue(new QueuedInstallation.ModInstall(full));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(installedReason,
				Is.EqualTo(expectedAvailable ? null : new ModCardViewModel.InstallUnavailableReason.OtherModInstalled("Old")));
			Assert.That(sut.IsAvailable, Is.EqualTo(expectedAvailable));
			Assert.That(sut.UnavailableReason,
				Is.EqualTo(expectedAvailable ? null : new ModCardViewModel.InstallUnavailableReason.OtherModInstalled("Full")));
		});
	}

	[Test]
	public async Task UninstallCommand_InstalledMod_UninstallsAndAllowsInstallAgainEvenOnFailure()
	{
		// Arrange
		using var context = new CardContext();
		var mod = Mod("Example");
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true);
		context.Installations.Setup(x => x.UninstallMod(mod, It.IsAny<CancellationToken>()))
			.Callback(() => context.States[mod] = ModInstallState.NotInstalled)
			.ThrowsAsync(new IOException("locked"));
		var sut = context.Card(mod);
		var canInstallBefore = sut.InstallCommand.CanExecute(null);

		// Act
		await Assert.ThrowsAsync<IOException>(() => sut.UninstallCommand.ExecuteAsync(null));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(canInstallBefore, Is.False);
			Assert.That(sut.IsInstalled, Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public void UninstallCommand_InstalledVersionWithoutUninstallSupport_CannotExecute()
	{
		using var context = new CardContext();
		var mod = Mod("Example");
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: false);
		var sut = context.Card(mod);

		Assert.Multiple(() =>
		{
			Assert.That(sut.IsUninstallSupported, Is.False);
			Assert.That(sut.UninstallCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public async Task UninstallCommand_UpdateRunning_CannotExecuteUntilUpdateCompletes()
	{
		// Arrange
		using var context = new CardContext();
		var mod = Mod("Example");
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true);
		var completion = new TaskCompletionSource();
		context.Installations.Setup(x => x.InstallMod(mod, It.IsAny<ModCategoryFeedData>(), NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
			.Returns(completion.Task);
		var sut = context.Card(mod);

		// Act
		var updating = sut.InstallCommand.ExecuteAsync(null);
		var canUninstallWhileUpdating = sut.UninstallCommand.CanExecute(null);
		completion.SetResult();
		await updating;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(canUninstallWhileUpdating, Is.False);
			Assert.That(sut.UninstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public void Deactivated_IgnoresChanges_AndReactivationReadsCurrentState()
	{
		// Arrange
		using var context = new CardContext();
		var sut = context.Card(Mod("Example"), activate: false);
		var activation = CardContext.Activate(sut);

		// Act
		activation.Dispose();
		context.Queue(QueuedInstallation.Modpack.Instance);
		var availableWhileInactive = sut.IsAvailable;
		using var reactivation = CardContext.Activate(sut);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(availableWhileInactive, Is.True);
			Assert.That(sut.IsAvailable, Is.False);
			Assert.That(sut.UnavailableReason, Is.EqualTo(ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued.Instance));
		});
	}

	[Test]
	public void ChangePostedBeforeDeactivation_DoesNotUpdateTheInactiveCard()
	{
		// Arrange
		using var context = new CardContext();
		var dispatcher = new QueuedUiDispatcher();
		var sut = context.Card(Mod("Example"), dispatcher: dispatcher, activate: false);
		var activation = CardContext.Activate(sut);
		context.Queue(QueuedInstallation.Modpack.Instance);

		// Act
		activation.Dispose();
		dispatcher.RunAll();

		// Assert
		Assert.That(sut.IsAvailable, Is.True);
	}

	[Test]
	public void Activation_FinishedRaisedBetweenSubscribeAndSnapshot_CardEndsWithCurrentState()
	{
		// Arrange: the mod's own installation finishes right after the activation first reads the queue.
		using var context = new CardContext();
		var mod = Mod("Example");
		var installing = new QueuedInstallation.ModInstall(mod);
		context.Queue(installing);
		var isFinished = false;
		context.Installations.SetupGet(x => x.QueuedInstallations).Returns(() =>
		{
			var snapshot = context.Snapshot();
			if (!isFinished)
			{
				isFinished = true;
				context.Finish(installing);
			}

			return snapshot;
		});
		var dispatcher = new QueuedUiDispatcher();
		var sut = context.Card(mod, dispatcher: dispatcher, activate: false);

		// Act
		using var activation = CardContext.Activate(sut);
		var availableBeforeDispatch = sut.InstallCommand.CanExecute(null);
		dispatcher.RunAll();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(isFinished, Is.True);
			Assert.That(availableBeforeDispatch, Is.False);
			Assert.That(sut.InstallCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public void DescriptionUri_AndActionFacts_AreExposedWithoutUiRequests()
	{
		using var context = new CardContext();
		var mod = Mod("Example", requiresNewGame: true);
		var sut = context.Card(mod);
		Assert.Multiple(() =>
		{
			Assert.That(sut.Name, Is.EqualTo("Example"));
			Assert.That(sut.LogoUri, Is.EqualTo(new Uri("https://feed.test/Example.png")));
			Assert.That(sut.DescriptionUri, Is.EqualTo(new Uri("https://feed.test/Example.md")));
			Assert.That(sut.InstallRequiresNewGame, Is.True);
			Assert.That(sut.UninstallRequiresNewGame, Is.True);
		});
		context.States[mod] = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true);
		context.Queue(QueuedInstallation.Modpack.Instance);
		Assert.That(sut.InstallRequiresNewGame, Is.False);
	}

	[Test]
	public void UnavailableFactTexts_MapTypedCasesToDesktopResourceKeysAndRawName()
	{
		var strings = new Mock<IStringLocalizer<DesktopStrings>>(MockBehavior.Strict);
		strings.Setup(x => x[It.IsAny<string>()]).Returns<string>(key => new LocalizedString(key, key));
		strings.Setup(x => x[DesktopStrings.OtherModInstalled, It.Is<object[]>(args => args.Length == 1 && Equals(args[0], "Other"))])
			.Returns(new LocalizedString(DesktopStrings.OtherModInstalled, DesktopStrings.OtherModInstalled));
		var texts = new HomeCardFactTexts(strings.Object);
		Assert.Multiple(() =>
		{
			Assert.That(texts.Get(ModpackInstallUnavailableReason.NoAvailableVersions).Name, Is.EqualTo(DesktopStrings.ModpackInstallUnavailable));
			Assert.That(texts.Get(ModCardViewModel.InstallUnavailableReason.NotUpdated.Instance).Name, Is.EqualTo(DesktopStrings.ModNotUpdated));
			Assert.That(new ChoiceTexts(strings.Object).Get(ChoiceText.UnavailableModNotUpdated.Instance).Name, Is.EqualTo(DesktopStrings.ModNotUpdated));
			Assert.That(texts.Get(ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued.Instance).Name, Is.EqualTo(DesktopStrings.ModpackTaskQueued));
			Assert.That(texts.Get(new ModCardViewModel.InstallUnavailableReason.OtherModInstalled("Other")).Name, Is.EqualTo(DesktopStrings.OtherModInstalled));
		});
		strings.Verify(x => x[DesktopStrings.OtherModInstalled, It.IsAny<object[]>()], Times.Once);
	}

	private static ModFeedData Mod(string name, string supportedVersion = "[3.0.0, 4.0.0)", bool requiresNewGame = false) => new()
	{
		Name = name,
		CategoryId = "mods",
		LogoPath = $"{name}.png",
		DescriptionPath = new() { ["ru"] = $"{name}.md" },
		RequiresNewGame = requiresNewGame,
		Items =
		[
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = $"{name}.7z" },
				SupportedModpackVersion = VersionRange.Parse(supportedVersion),
			},
		],
	};

	/// <summary>A mocked installation service whose queued installations and installed facts the test sets.</summary>
	private sealed class CardContext : IDisposable
	{
		private readonly Subject<InstallationChange> _changes = new();
		private readonly CompositeDisposable _activations = [];
		private readonly List<QueuedInstallation> _queued = [];
		private readonly Mock<IModpackVersionProvider> _modpackVersion = new(MockBehavior.Strict);
		private readonly Mock<IFeedUtils> _feedUtils = new(MockBehavior.Strict);

		public Mock<IInstallationService> Installations { get; } = new(MockBehavior.Strict);
		public Dictionary<ModFeedData, ModInstallState> States { get; } = [];

		public CardContext()
		{
			Installations.SetupGet(x => x.Changes).Returns(_changes);
			Installations.SetupGet(x => x.QueuedInstallations).Returns(() => _queued.ToArray());
			Installations.Setup(x => x.GetModInstallState(It.IsAny<ModFeedData>()))
				.Returns<ModFeedData>(m => States.GetValueOrDefault(m, ModInstallState.NotInstalled));
			_modpackVersion.SetupGet(x => x.InstalledModpackVersion).Returns(ModpackVersion);
			_feedUtils.Setup(x => x.GetUri(It.IsAny<string>())).Returns<string>(p => new Uri("https://feed.test/" + p));
		}

		/// <summary>Creates the card and, unless <paramref name="activate"/> is false, activates it until disposal.</summary>
		public ModCardViewModel Card(ModFeedData mod, bool isExclusive = false, IReadOnlyList<ModFeedData>? categoryMods = null,
			IUiDispatcher? dispatcher = null, bool activate = true)
		{
			var card = new ModCardViewModel(mod,
				new ModCategoryFeedData { Id = "mods", Title = new() { ["ru"] = "Mods" }, IsExclusive = isExclusive },
				categoryMods ?? [mod], Installations.Object, _modpackVersion.Object, _feedUtils.Object,
				dispatcher ?? HomeCardsDispatcher.Instance);
			if (activate)
				_activations.Add(Activate(card));
			return card;
		}

		/// <summary>Activates the card as its view does; activation runs synchronously.</summary>
		public static IDisposable Activate(ModCardViewModel card)
		{
			var activation = card.Activator.Activate(CancellationToken.None).AsTask();
			Assert.That(activation.IsCompletedSuccessfully, Is.True);
			return activation.Result;
		}

		public QueuedInstallation[] Snapshot() => _queued.ToArray();

		public void Queue(QueuedInstallation installation)
		{
			_queued.Add(installation);
			_changes.OnNext(new InstallationChange.Queued(installation));
		}

		public void Finish(QueuedInstallation installation)
		{
			_queued.Remove(installation);
			_changes.OnNext(new InstallationChange.Finished(installation));
		}

		public void Dispose()
		{
			_activations.Dispose();
			_changes.Dispose();
		}
	}

	/// <summary>Runs posted actions only when asked, in posting order, as a UI dispatcher does after the current work.</summary>
	private sealed class QueuedUiDispatcher : IUiDispatcher
	{
		private readonly Queue<Action> _actions = new();

		public void Post(Action action) => _actions.Enqueue(action);

		public void RunAll()
		{
			while (_actions.TryDequeue(out var action))
				action();
		}
	}
}
