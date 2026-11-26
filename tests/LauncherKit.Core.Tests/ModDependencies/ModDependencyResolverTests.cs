using NuGet.Versioning;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.ModDependencies;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.ModDependencies;

[TestFixture]
public sealed class ModDependencyResolverTests
{
	private static readonly NuGetVersion ModpackVersion = NuGetVersion.Parse("3.0.0");

	[Test]
	public async Task Resolve_NoDependencies_PlansNothing()
	{
		// Arrange
		var addon = Mod("Addon", Item("1.0.0"));
		var sut = CreateResolver([addon], NoChoice);

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan, Is.Empty);
	}

	[Test]
	public async Task Resolve_SingleAlternativeNotInstalled_PlansGreatestReachablePatchInRangeWithoutAsking()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0", patches: ["1.0.1", "1.0.2"]), Item("2.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 2.0.0)"))));
		var sut = CreateResolver([library, addon], NoChoice);

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Select(x => (x.Mod.Name, x.Item.Version.ToString(), x.TargetVersion.ToString(), x.InstalledVersion)),
			Is.EqualTo(new[] { ("Library", "1.0.0", "1.0.2", (NuGetVersion?)null) }));
	}

	[Test]
	public async Task Resolve_RangeUpperBoundBetweenPatches_StopsAtLastPatchInRange()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0", patches: ["1.0.1", "1.0.2"]));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 1.0.2)"))));
		var sut = CreateResolver([library, addon], NoChoice);

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Single().TargetVersion, Is.EqualTo(NuGetVersion.Parse("1.0.1")));
	}

	[Test]
	public async Task Resolve_InRangeVersionDoesNotSupportModpack_PlansSupportedOne()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"), Item("1.5.0", supportedModpack: "[4.0.0, )"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 2.0.0)"))));
		var sut = CreateResolver([library, addon], NoChoice);

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Single().TargetVersion, Is.EqualTo(NuGetVersion.Parse("1.0.0")));
	}

	[Test]
	public async Task Resolve_InstalledInRange_PlansNothingWithoutAsking()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"), Item("2.0.0"));
		var alternative = Mod("Alternative", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 2.0.0)"), ("Alternative", "[1.0.0, )"))));
		var sut = CreateResolver([library, alternative, addon], NoChoice,
			installed: new() { ["Alternative"] = new InstalledModVersion(NuGetVersion.Parse("1.0.0"), IsFinished: true) });

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan, Is.Empty);
	}

	[Test]
	public async Task Resolve_UnfinishedInstallationInRange_PlansItsReinstallation()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )"))));
		var sut = CreateResolver([library, addon], NoChoice,
			installed: new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("1.0.0"), IsFinished: false) });

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Single().InstalledVersion, Is.EqualTo(NuGetVersion.Parse("1.0.0")));
	}

	[Test]
	public async Task Resolve_SeveralAlternatives_AsksWithUnavailableOptionsAndPlansTheChosenOne()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"));
		var unsupported = Mod("Unsupported", Item("1.0.0", supportedModpack: "[4.0.0, )"));
		var alternative = Mod("Alternative", Item("1.0.0"), Item("1.1.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0",
			Requires(("Library", "[1.0.0, )"), ("Unsupported", "[1.0.0, )"), ("Alternative", "[1.0.0, )"))));
		ModDependencyChoice? asked = null;
		var sut = CreateResolver([library, unsupported, alternative, addon], (choice, _) =>
		{
			asked = choice;
			return ValueTask.FromResult(choice.Options[2]);
		});

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked!.Dependent, Is.SameAs(addon));
			Assert.That(asked.Options.Select(x => x.IsAvailable), Is.EqualTo(new[] { true, false, true }));
			Assert.That(asked.Options[1].UnavailableReason,
				Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.NoSupportedVersion(VersionRange.Parse("[1.0.0, )"))));
			Assert.That(plan.Select(x => (x.Mod.Name, x.TargetVersion.ToString())), Is.EqualTo(new[] { ("Alternative", "1.1.0") }));
		});
	}

	[Test]
	public async Task Resolve_InstalledOutOfRangeOlder_OffersItsUpgradeBesideOtherAlternative()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"), Item("2.0.0"));
		var alternative = Mod("Alternative", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[2.0.0, )"), ("Alternative", "[1.0.0, )"))));
		ModDependencyChoice? asked = null;
		var sut = CreateResolver([library, alternative, addon], (choice, _) =>
		{
			asked = choice;
			return ValueTask.FromResult(choice.Options[0]);
		}, installed: new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("1.0.0"), IsFinished: true) });

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked!.Options.All(x => x.IsAvailable));
			Assert.That(plan.Select(x => (x.Mod.Name, x.TargetVersion.ToString(), x.InstalledVersion?.ToString())),
				Is.EqualTo(new[] { ("Library", "2.0.0", "1.0.0") }));
		});
	}

	[Test]
	public async Task Resolve_InstalledNewerThanRange_ThrowsWithoutAsking()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"), Item("2.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 2.0.0)"))));
		var sut = CreateResolver([library, addon], NoChoice,
			installed: new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("2.0.0"), IsFinished: true) });

		// Act
		var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(() => Resolve(sut, addon));

		// Assert
		Assert.That(exception!.Alternatives.Single().UnavailableReason,
			Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.NewerVersionInstalled(NuGetVersion.Parse("2.0.0"))));
	}

	[Test]
	public async Task Resolve_InstalledVersionNotInFeed_IsUnavailable()
	{
		// Arrange
		var library = Mod("Library", Item("2.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[2.0.0, )"))));
		var sut = CreateResolver([library, addon], NoChoice,
			installed: new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("1.5.0"), IsFinished: true) });

		// Act
		var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(() => Resolve(sut, addon));

		// Assert
		Assert.That(exception!.Alternatives.Single().UnavailableReason,
			Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.UnknownInstalledVersion(NuGetVersion.Parse("1.5.0"))));
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	public async Task Resolve_OtherModOfExclusiveCategoryInstalledOrQueued_IsUnavailable(bool installed, bool queued)
	{
		// Arrange
		var pack = Mod("Pack", Item("1.0.0"), categoryId: "packs");
		var otherPack = Mod("Other Pack", Item("1.0.0"), categoryId: "packs");
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Pack", "[1.0.0, )"))));
		var sut = CreateResolver([pack, otherPack, addon], NoChoice,
			installed: installed
				? new() { ["Other Pack"] = new InstalledModVersion(NuGetVersion.Parse("1.0.0"), IsFinished: true) }
				: null,
			queuedInstalls: queued ? ["Other Pack"] : []);

		// Act
		var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(() => Resolve(sut, addon));

		// Assert
		Assert.That(exception!.Alternatives.Single().UnavailableReason,
			Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.OtherModInExclusiveCategory(otherPack)));
	}

	[Test]
	public async Task Resolve_TransitiveChain_PlansDependenciesBeforeTheModsNeedingThem()
	{
		// Arrange
		var core = Mod("Core", Item("1.0.0"));
		var library = Mod("Library", ItemRequiring("1.0.0", Requires(("Core", "[1.0.0, )"))));
		var other = Mod("Other", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )")), Requires(("Other", "[1.0.0, )"))));
		var sut = CreateResolver([core, library, other, addon], NoChoice);

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Select(x => x.Mod.Name), Is.EqualTo(new[] { "Core", "Library", "Other" }));
	}

	[Test]
	public async Task Resolve_SharedRequirementMetByPlannedVersion_PlansItOnce()
	{
		// Arrange
		var core = Mod("Core", Item("1.0.0"), Item("1.5.0"));
		var library = Mod("Library", ItemRequiring("1.0.0", Requires(("Core", "[1.0.0, )"))));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )")), Requires(("Core", "[1.5.0, 2.0.0)"))));
		var sut = CreateResolver([core, library, addon], NoChoice);

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Select(x => (x.Mod.Name, x.TargetVersion.ToString())),
			Is.EqualTo(new[] { ("Core", "1.5.0"), ("Library", "1.0.0") }));
	}

	[Test]
	public async Task Resolve_SharedRequirementConflictsWithPlannedVersion_Throws()
	{
		// Arrange
		var core = Mod("Core", Item("1.0.0"), Item("2.0.0"));
		var library = Mod("Library", ItemRequiring("1.0.0", Requires(("Core", "[2.0.0, )"))));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )")), Requires(("Core", "[1.0.0, 2.0.0)"))));
		var sut = CreateResolver([core, library, addon], NoChoice);

		// Act
		var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(() => Resolve(sut, addon));

		// Assert
		Assert.That(exception!.Alternatives.Single().UnavailableReason,
			Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.ConflictingVersion(NuGetVersion.Parse("2.0.0"))));
	}

	[Test]
	public async Task Resolve_Cycle_Throws()
	{
		// Arrange
		var library = Mod("Library", ItemRequiring("1.0.0", Requires(("Addon", "[1.0.0, )"))));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )"))));
		var sut = CreateResolver([library, addon], NoChoice);

		// Act
		var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(() => Resolve(sut, addon));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(exception!.Mod, Is.SameAs(library));
			Assert.That(exception!.Alternatives.Single().UnavailableReason,
				Is.EqualTo((ModDependencyUnavailableReason)ModDependencyUnavailableReason.Cycle.Instance));
		});
	}

	[Test]
	public async Task Resolve_InstalledVersionMeetsOneEntryButAnotherNeedsVersionOutsideItsRange_Throws()
	{
		// Arrange
		var library = Mod("Library", Item("1.5.0"), Item("2.5.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 2.0.0)")), Requires(("Library", "[2.0.0, 3.0.0)"))));
		var sut = CreateResolver([library, addon], NoChoice,
			installed: new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("1.5.0"), IsFinished: true) });

		// Act
		var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(() => Resolve(sut, addon));

		// Assert
		Assert.That(exception!.Alternatives.Single().UnavailableReason,
			Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.ConflictingVersion(NuGetVersion.Parse("1.5.0"))));
	}

	[Test]
	public async Task Resolve_InstalledVersionMeetsOneEntryAndAnotherNeedsNewerOneInItsRange_UpdatesWithinBothRanges()
	{
		// Arrange
		var library = Mod("Library", Item("1.5.0"), Item("2.5.0"), Item("3.5.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 3.0.0)")), Requires(("Library", "[2.0.0, )"))));
		var sut = CreateResolver([library, addon], NoChoice,
			installed: new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("1.5.0"), IsFinished: true) });

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Select(x => (x.Mod.Name, x.TargetVersion.ToString(), x.InstalledVersion?.ToString())),
			Is.EqualTo(new[] { ("Library", "2.5.0", "1.5.0") }));
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task Resolve_OverlappingRangesOfSharedMod_PlansGreatestVersionInAllOfThemWhateverTheOrder(bool narrowFirst)
	{
		// Arrange
		var library = Mod("Library", Item("1.5.0"), Item("2.5.0"));
		var broad = Mod("Broad", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 3.0.0)"))));
		var narrow = Mod("Narrow", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 2.0.0)"))));
		ModDependency[] entries = narrowFirst
			? [Requires(("Narrow", "[1.0.0, )")), Requires(("Broad", "[1.0.0, )"))]
			: [Requires(("Broad", "[1.0.0, )")), Requires(("Narrow", "[1.0.0, )"))];
		var addon = Mod("Addon", ItemRequiring("1.0.0", entries));
		var sut = CreateResolver([library, broad, narrow, addon], NoChoice);

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.That(plan.Single(x => x.Mod.Name == "Library").TargetVersion, Is.EqualTo(NuGetVersion.Parse("1.5.0")));
	}

	[Test]
	public async Task Resolve_AlternativeWithUnsatisfiableTransitiveRequirement_IsOfferedDisabled()
	{
		// Arrange
		var missing = Mod("Missing", Item("1.0.0", supportedModpack: "[4.0.0, )"));
		var first = Mod("First", ItemRequiring("1.0.0", Requires(("Missing", "[1.0.0, )"))));
		var second = Mod("Second", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("First", "[1.0.0, )"), ("Second", "[1.0.0, )"))));
		ModDependencyChoice? asked = null;
		var sut = CreateResolver([missing, first, second, addon], (choice, _) =>
		{
			asked = choice;
			return ValueTask.FromResult(choice.Options[1]);
		});

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked!.Options.Select(x => x.IsAvailable), Is.EqualTo(new[] { false, true }));
			Assert.That(asked.Options[0].UnavailableReason,
				Is.EqualTo((ModDependencyUnavailableReason)ModDependencyUnavailableReason.UnsatisfiableDependencies.Instance));
			Assert.That(plan.Select(x => x.Mod.Name), Is.EqualTo(new[] { "Second" }));
		});
	}

	[Test]
	public async Task Resolve_AlternativeMetOnlyByKeepingItsInstalledVersion_IsOfferedAvailableAndKeptWhenChosen()
	{
		// Arrange: the newest Extra needs Library 2.5.0, out of the second entry's range; Extra 1.0.0 needs nothing, so a
		// plan meets the entry by keeping the installed Library 1.5.0.
		var library = Mod("Library", Item("1.5.0"), Item("2.5.0"));
		var extra = Mod("Extra", Item("1.0.0"), ItemRequiring("2.0.0", Requires(("Library", "[2.5.0, )"))));
		var alternative = Mod("Alternative", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Extra", "[1.0.0, )")),
			Requires(("Library", "[1.0.0, 2.0.0)"), ("Alternative", "[1.0.0, )"))));
		ModDependencyChoice? asked = null;
		var sut = CreateResolver([library, extra, alternative, addon], (choice, _) =>
		{
			asked = choice;
			return ValueTask.FromResult(choice.Options[0]);
		}, new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("1.5.0"), true) });

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked!.Options.Select(x => (x.IsAvailable, x.Install?.TargetVersion, x.KeptVersion)), Is.EqualTo(new[]
			{
				(true, (NuGetVersion?)null, (NuGetVersion?)NuGetVersion.Parse("1.5.0")),
				(true, (NuGetVersion?)NuGetVersion.Parse("1.0.0"), (NuGetVersion?)null),
			}));
			Assert.That(plan.Select(x => (x.Mod.Name, x.TargetVersion.ToString())), Is.EqualTo(new[] { ("Extra", "1.0.0") }));
		});
	}

	[Test]
	public async Task Resolve_ChoiceThenTransitiveChoice_AsksEachOnceAndPlansDependenciesFirst()
	{
		// Arrange
		var core = Mod("Core", Item("1.0.0"));
		var otherCore = Mod("Other Core", Item("1.0.0"));
		var library = Mod("Library", ItemRequiring("1.0.0", Requires(("Core", "[1.0.0, )"), ("Other Core", "[1.0.0, )"))));
		var alternative = Mod("Alternative", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )"), ("Alternative", "[1.0.0, )"))));
		var asked = new List<string>();
		var sut = CreateResolver([core, otherCore, library, alternative, addon], (choice, _) =>
		{
			asked.Add(choice.Dependent.Name);
			return ValueTask.FromResult(choice.Dependent == addon ? choice.Options[0] : choice.Options[1]);
		});

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked, Is.EqualTo(new[] { "Addon", "Library" }));
			Assert.That(plan.Select(x => x.Mod.Name), Is.EqualTo(new[] { "Other Core", "Library" }));
		});
	}

	[Test]
	public async Task Resolve_LaterOptionNeedsVersionWhoseOwnEntryMetTheDecidedOne_KeepsTheDecidedAlternative()
	{
		// Arrange
		var x = Mod("X", ItemRequiring("1.0.0", Requires(("A", "[1.0.0, )"))), Item("2.0.0"));
		var a = Mod("A", Item("1.0.0"));
		var b = Mod("B", Item("1.0.0"));
		var c = Mod("C", Item("1.0.0"));
		var d = Mod("D", ItemRequiring("1.0.0", Requires(("X", "[1.0.0, 2.0.0)"))));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("X", "[1.0.0, )")), Requires(("A", "[1.0.0, )"), ("B", "[1.0.0, )")),
			Requires(("C", "[1.0.0, )"), ("D", "[1.0.0, )"))));
		var asked = new List<ModDependencyChoice>();
		var sut = CreateResolver([x, a, b, c, d, addon], (choice, _) =>
		{
			asked.Add(choice);
			return ValueTask.FromResult(choice.Options[1]);
		});

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked.Select(choice => choice.Index), Is.EqualTo(new[] { 1, 2 }));
			Assert.That(plan.Select(install => (install.Mod.Name, install.TargetVersion.ToString())),
				Is.EquivalentTo(new[] { ("X", "1.0.0"), ("A", "1.0.0"), ("B", "1.0.0"), ("D", "1.0.0") }));
		});
	}

	[Test]
	public async Task Resolve_LaterOptionWouldDropTheEntryOfAnEarlierDecision_IsUnavailable()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"), ItemRequiring("2.0.0", Requires(("P", "[1.0.0, )"), ("Q", "[1.0.0, )"))));
		var p = Mod("P", Item("1.0.0"));
		var q = Mod("Q", Item("1.0.0"));
		var r = Mod("R", Item("1.0.0"));
		var s = Mod("S", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, 2.0.0)"))));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )")),
			Requires(("R", "[1.0.0, )"), ("S", "[1.0.0, )"))));
		ModDependencyChoice? second = null;
		var sut = CreateResolver([library, p, q, r, s, addon], (choice, _) =>
		{
			if (choice.Dependent == library)
			{
				return ValueTask.FromResult(choice.Options[1]);
			}

			second = choice;
			return ValueTask.FromResult(choice.Options[0]);
		});

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(second!.Options.Select(option => option.IsAvailable), Is.EqualTo(new[] { true, false }));
			Assert.That(plan.Select(install => install.Mod.Name), Is.EquivalentTo(new[] { "Q", "Library", "R" }));
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task Resolve_InstalledAlternativeCannotStayInRange_PlansAnotherAlternativeWhateverTheOrder(bool choiceFirst)
	{
		// Arrange
		var library = Mod("Library", Item("1.5.0"), Item("2.5.0"));
		var alternative = Mod("Alternative", Item("1.0.0"));
		var choiceEntry = Requires(("Library", "[1.0.0, 2.0.0)"), ("Alternative", "[1.0.0, )"));
		var libraryEntry = Requires(("Library", "[2.0.0, 3.0.0)"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", choiceFirst ? [choiceEntry, libraryEntry] : [libraryEntry, choiceEntry]));
		ModDependencyChoice? asked = null;
		var sut = CreateResolver([library, alternative, addon], (choice, _) =>
		{
			asked = choice;
			return ValueTask.FromResult(choice.Options.Single(option => option.IsAvailable));
		}, installed: new() { ["Library"] = new InstalledModVersion(NuGetVersion.Parse("1.5.0"), IsFinished: true) });

		// Act
		var plan = await Resolve(sut, addon);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked!.Options.Select(option => (option.Mod.Name, option.IsAvailable)),
				Is.EqualTo(new[] { ("Library", false), ("Alternative", true) }));
			Assert.That(plan.Select(install => (install.Mod.Name, install.TargetVersion.ToString(), install.InstalledVersion?.ToString())),
				Is.EquivalentTo(new[] { ("Library", "2.5.0", "1.5.0"), ("Alternative", "1.0.0", (string?)null) }));
		});
	}

	[Test]
	public async Task Resolve_SearchAfterChoice_RunsOffTheThreadThatAnswered()
	{
		// Arrange
		var library = Mod("Library", Item("1.0.0"));
		var alternative = Mod("Alternative", Item("1.0.0"));
		var addon = Mod("Addon", ItemRequiring("1.0.0", Requires(("Library", "[1.0.0, )"), ("Alternative", "[1.0.0, )"))));
		var answered = false;
		var searchThreads = new System.Collections.Concurrent.ConcurrentBag<int>();
		var mods = new EnumerationHookList([library, alternative, addon], _ =>
		{
			if (Volatile.Read(ref answered))
			{
				searchThreads.Add(Environment.CurrentManagedThreadId);
			}
		});
		var answer = new ObservedAnswer();
		ModDependencyChoice? asked = null;
		var sut = CreateResolver(mods, (choice, _) =>
		{
			asked = choice;
			return answer.Task;
		});
		var resolving = Task.Run(() => Resolve(sut, addon));
		await TaskAssert.WithinTimeout(answer.Awaited.Task);
		var answeringThreadId = 0;
		var answeringThread = new Thread(() =>
		{
			answeringThreadId = Environment.CurrentManagedThreadId;
			Volatile.Write(ref answered, true);
			answer.SetResult(asked!.Options[1]);
		});

		// Act
		answeringThread.Start();
		answeringThread.Join();
		var plan = await TaskAssert.WithinTimeout(resolving);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(plan.Single().Mod, Is.EqualTo(alternative));
			Assert.That(searchThreads, Is.Not.Empty);
			Assert.That(searchThreads, Has.None.EqualTo(answeringThreadId));
		});
	}

	private static ValueTask<ModDependencyOption> NoChoice(ModDependencyChoice choice, CancellationToken cancellationToken) =>
		throw new AssertionException("No choice expected");

	private static async Task<IReadOnlyList<PlannedModInstall>> Resolve(ModDependencyResolver sut, ModFeedData mod) =>
		await sut.Resolve(mod, mod.Items[^1], mod.Items[^1].GetLatestVersion(), CancellationToken.None);

	private static ModDependencyResolver CreateResolver(IReadOnlyList<ModFeedData> mods,
		Func<ModDependencyChoice, CancellationToken, ValueTask<ModDependencyOption>> choose,
		Dictionary<string, InstalledModVersion>? installed = null, IReadOnlyCollection<string>? queuedInstalls = null) =>
		new(mods,
			[
				new ModCategoryFeedData { Id = "mods", Title = new() { ["ru"] = "Mods" } },
				new ModCategoryFeedData { Id = "packs", Title = new() { ["ru"] = "Packs" }, IsExclusive = true },
			],
			ModpackVersion, installed ?? [], queuedInstalls ?? [], choose);

	private static ModDependency Requires(params (string ModName, string Range)[] alternatives) =>
		new ModDependency.AnotherMod(alternatives.Select(x => new ModDependencyAlternative(x.ModName, VersionRange.Parse(x.Range))).ToArray());

	private static ModFeedItem ItemRequiring(string version, params ModDependency[] dependencies) =>
		Item(version, dependencies: dependencies);

	private static ModFeedItem Item(string version, string[]? patches = null, string supportedModpack = "[1.0.0, )",
		ModDependency[]? dependencies = null) => new()
	{
		Version = NuGetVersion.Parse(version),
		ArchiveDownloadInfo = new DownloadInfo { S3Path = $"Mods/{version}.7z" },
		Patches = patches?.Select(x => new ModPatchFeedItem
		{
			Version = NuGetVersion.Parse(x),
			DownloadInfo = new DownloadInfo { S3Path = $"Mods/{x}.7z" },
		}).ToArray(),
		Dependencies = dependencies is { Length: > 0 } ? dependencies : null,
		SupportedModpackVersion = VersionRange.Parse(supportedModpack),
	};

	private static ModFeedData Mod(string name, params ModFeedItem[] items) => Mod(name, items, "mods");

	private static ModFeedData Mod(string name, ModFeedItem item, string categoryId) => Mod(name, [item], categoryId);

	private static ModFeedData Mod(string name, IReadOnlyList<ModFeedItem> items, string categoryId) => new()
	{
		Name = name,
		CategoryId = categoryId,
		LogoPath = $"{name}.png",
		DescriptionPath = new() { ["ru"] = $"{name}.md" },
		Items = items,
	};

	/// <summary>
	/// An answer to a choice whose continuation runs on the thread that sets it, and that tells when it is awaited.
	/// </summary>
	private sealed class ObservedAnswer : System.Threading.Tasks.Sources.IValueTaskSource<ModDependencyOption>
	{
		private System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<ModDependencyOption> _core;

		public TaskCompletionSource Awaited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public ValueTask<ModDependencyOption> Task => new(this, _core.Version);

		public void SetResult(ModDependencyOption option) => _core.SetResult(option);

		public ModDependencyOption GetResult(short token) => _core.GetResult(token);

		public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

		public void OnCompleted(Action<object?> continuation, object? state, short token,
			System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)
		{
			_core.OnCompleted(continuation, state, token, flags);
			Awaited.SetResult();
		}
	}
}
