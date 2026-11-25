using NuGet.Versioning;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.ModDependencies;

/// <summary>
/// Plans the mods to install before a requested mod version so that every <see cref="ModFeedItem.Dependencies"/> entry
/// of it and of the planned versions is met by the final plan, asking which alternative to install where an entry
/// declares several.
/// </summary>
/// <remarks>
/// <para>
/// An entry the user decided is met only by the chosen alternative, and a plan is accepted only when every decided entry
/// is part of it, so later searches never drop a confirmed choice. Otherwise an entry is met by an alternative whose
/// mod the plan already settled (planned, or installed and kept) with a version in its range. Otherwise, an alternative
/// installed (and finished) with a version in range is preferred and met without asking: the installed version is kept,
/// or, when another entry of the plan needs it, updated within the range; only when no complete plan keeps that
/// preference are the remaining alternatives tried. Otherwise the user chooses one alternative of an entry with
/// several. A mod is planned with the greatest version (a version supporting the modpack plus a prefix of its patches)
/// for which every entry of the plan can still be met, so overlapping ranges of one mod settle on the greatest version
/// in all of them, and the planned version's own entries are met before it.
/// </para>
/// <para>
/// A choice offers only the alternatives with which the whole plan, transitive entries and earlier decisions included,
/// can be met; the others carry their <see cref="ModDependencyUnavailableReason"/>. A plan never downgrades a mod,
/// changes an installed version the feed does not list, changes a version another entry was met with, plans a mod
/// twice, makes mods wait for each other (a cycle) or installs a mod of an exclusive category while another one of it
/// is installed, planned or queued for install.
/// </para>
/// <para>
/// The search can take long, so it runs on the thread pool (never on the caller's thread or on the thread that answered
/// a choice) and stops when the cancellation token is cancelled.
/// </para>
/// </remarks>
public sealed class ModDependencyResolver
{
	private readonly IReadOnlyList<ModFeedData> _mods;
	private readonly IReadOnlyList<ModCategoryFeedData> _categories;
	private readonly NuGetVersion _modpackVersion;
	private readonly IReadOnlyDictionary<string, InstalledModVersion> _installedMods;
	private readonly IReadOnlyCollection<string> _queuedInstalls;
	private readonly Func<ModDependencyChoice, CancellationToken, ValueTask<ModDependencyOption>> _choose;

	/// <summary>The settled mods of the plan being searched: the requested mod, planned and kept installed mods.</summary>
	private readonly Dictionary<string, Settled> _settled = new(StringComparer.Ordinal);

	/// <summary>The planned mods each mod waits for, in the order of its entries.</summary>
	private readonly Dictionary<string, List<string>> _waitsFor = new(StringComparer.Ordinal);

	/// <summary>Undecided choices on the searched path, outermost first.</summary>
	private readonly List<ChoicePoint> _choicePath = [];

	private readonly Dictionary<GroupKey, string> _decisions = [];
	private string _requestedModName = string.Empty;
	private Solution? _solution;
	private ModDependencyUnavailableException? _firstFailure;
	private bool _isResolved;
	private CancellationToken _cancellationToken;

	/// <summary>How many decided entries the searched path met so far.</summary>
	private int _metDecisions;

	/// <param name="mods">The feed's mods.</param>
	/// <param name="categories">The feed's mod categories.</param>
	/// <param name="modpackVersion">The installed modpack version planned versions must support.</param>
	/// <param name="installedMods">
	/// The installed mods by name. Rolling back an installed version keeps the mods the plan declares, so none of them
	/// is removed before the plan installs.
	/// </param>
	/// <param name="queuedInstalls">Names of other mods queued for installation after the requested one.</param>
	/// <param name="choose">Asks which available option of a choice to install.</param>
	public ModDependencyResolver(IReadOnlyList<ModFeedData> mods, IReadOnlyList<ModCategoryFeedData> categories,
		NuGetVersion modpackVersion, IReadOnlyDictionary<string, InstalledModVersion> installedMods,
		IReadOnlyCollection<string> queuedInstalls,
		Func<ModDependencyChoice, CancellationToken, ValueTask<ModDependencyOption>> choose)
	{
		_mods = mods;
		_categories = categories;
		_modpackVersion = modpackVersion;
		_installedMods = installedMods;
		_queuedInstalls = queuedInstalls;
		_choose = choose;
	}

	/// <summary>
	/// The installations that must precede installing <paramref name="item"/> of <paramref name="mod"/> (which reaches
	/// <paramref name="version"/>), dependencies before the mods that need them. Throws
	/// <see cref="ModDependencyUnavailableException"/> when the entries cannot all be met. A resolver plans only once.
	/// </summary>
	public async ValueTask<IReadOnlyList<PlannedModInstall>> Resolve(ModFeedData mod, ModFeedItem item, NuGetVersion version,
		CancellationToken cancellationToken)
	{
		if (_isResolved)
		{
			throw new InvalidOperationException("The resolver has already planned.");
		}

		_isResolved = true;
		_requestedModName = mod.Name;
		_cancellationToken = cancellationToken;
		var requested = new Settled(version, new PlannedModInstall(mod, item, version, null));
		var round = await Task.Run(() => Plan(requested), cancellationToken);

		// Every decision keeps the plan satisfiable, so each round asks one more choice until the plan has none left.
		while (round.Choice is { } choice)
		{
			var chosen = await _choose(choice, cancellationToken);
			if (!choice.Options.Contains(chosen) || !chosen.IsAvailable)
			{
				throw new InvalidOperationException("The chosen dependency option is not an available option of the choice.");
			}

			_decisions.Add(round.Key!, chosen.Mod.Name);
			round = await Task.Run(() => Plan(requested), cancellationToken);
		}

		return round.Plan;
	}

	/// <summary>The plan meeting the decisions taken and, while it has an undecided choice, its first one with its options.</summary>
	private Round Plan(Settled requested)
	{
		if (Search(requested) is not { } solution)
		{
			if (_decisions.Count > 0)
			{
				throw new InvalidOperationException("A decided choice left the dependencies unsatisfiable.");
			}

			throw _firstFailure ?? throw new InvalidOperationException("The search failed without an unmet entry.");
		}

		if (solution.FirstChoice is not { } choice)
		{
			return new Round(solution.Plan, null, null);
		}

		var options = choice.Alternatives
			.Select((alternative, index) => CreateOption(requested, choice, alternative, choice.Reasons[index]))
			.ToArray();
		return new Round(solution.Plan, choice.Key, new ModDependencyChoice(choice.Dependent, choice.Key.Index, options));
	}

	/// <summary>
	/// The option of <paramref name="alternative"/>, available when a plan meets the entry with it together with every
	/// earlier decision: installing it, or keeping its installed version.
	/// </summary>
	private ModDependencyOption CreateOption(Settled requested, ChoicePoint choice, ModDependencyAlternative alternative,
		ModDependencyUnavailableReason? reason)
	{
		var mod = FindMod(alternative.ModName);
		_decisions.Add(choice.Key, mod.Name);
		var solution = Search(requested);
		_decisions.Remove(choice.Key);
		if (solution == null)
		{
			return new ModDependencyOption(mod, null, null, reason ?? ModDependencyUnavailableReason.UnsatisfiableDependencies.Instance);
		}

		// The decided entry is met in the plan, so the mod is either planned or kept installed.
		return solution.Installs.GetValueOrDefault(mod.Name) is { } install
			? new ModDependencyOption(mod, install, null, null)
			: new ModDependencyOption(mod, null, solution.Kept[mod.Name], null);
	}

	/// <summary>The first plan, in search order, meeting every entry and the decisions taken, or <see langword="null"/>.</summary>
	private Solution? Search(Settled requested)
	{
		_settled.Clear();
		_waitsFor.Clear();
		_choicePath.Clear();
		_solution = null;
		_firstFailure = null;
		_metDecisions = 0;
		_settled.Add(_requestedModName, requested);
		Search(Push(requested.Install!, null));
		return _solution;
	}

	private bool Search(Agenda? agenda)
	{
		_cancellationToken.ThrowIfCancellationRequested();
		if (agenda == null)
		{
			// A decided entry missing from the plan (its dependent got another version) would drop the user's choice.
			if (_metDecisions < _decisions.Count)
			{
				return false;
			}

			_solution = CreateSolution();
			return true;
		}

		var (dependent, item, index) = agenda.Entry;
		var key = new GroupKey(dependent.Name, item.Version, index);
		var alternatives = item.Dependencies![index] switch
		{
			ModDependency.AnotherMod(var mods) => mods,
		};
		var isDecided = _decisions.TryGetValue(key, out var decided);
		IReadOnlyList<ModDependencyAlternative> allowed = isDecided
			? alternatives.Where(x => x.ModName == decided).ToArray()
			: alternatives;
		if (isDecided)
		{
			_metDecisions++;
		}

		if (SearchEntry(agenda, key, alternatives, allowed, isDecided))
		{
			return true;
		}

		if (isDecided)
		{
			_metDecisions--;
		}

		return false;
	}

	/// <summary>Meets the entry of <paramref name="agenda"/> with one of <paramref name="allowed"/>, then the rest of it.</summary>
	private bool SearchEntry(Agenda agenda, GroupKey key, IReadOnlyList<ModDependencyAlternative> alternatives,
		IReadOnlyList<ModDependencyAlternative> allowed, bool isDecided)
	{
		var dependent = agenda.Entry.Dependent;

		// Settled mods are not changed, so meeting the entry with one adds nothing that could fail later.
		foreach (var alternative in allowed)
		{
			if (_settled.TryGetValue(alternative.ModName, out var settled) && alternative.VersionRange.Satisfies(settled.Version) &&
			    !WouldCycle(dependent.Name, alternative.ModName, settled))
			{
				AddWait(dependent.Name, alternative.ModName, settled);
				if (Search(agenda.Next))
				{
					return true;
				}

				RemoveWait(dependent.Name, settled);
				return false;
			}
		}

		// Keeping (or updating within range) an installed alternative is preferred and needs no choice; the other
		// alternatives are tried only when no complete plan keeps it.
		var hasCandidate = false;
		var installedInRange = allowed.Where(IsInstalledInRange).ToArray();
		if (TrySettle(agenda, installedInRange, ref hasCandidate))
		{
			return true;
		}

		var remaining = allowed.Where(x => !installedInRange.Contains(x)).ToArray();
		var isChoice = !isDecided && alternatives.Count > 1 && remaining.Length > 0;
		if (isChoice)
		{
			_choicePath.Add(new ChoicePoint(key, dependent, alternatives,
				alternatives.Select(x => GetUnavailableReason(dependent.Name, x)).ToArray()));
		}

		if (TrySettle(agenda, remaining, ref hasCandidate))
		{
			return true;
		}

		if (isChoice)
		{
			_choicePath.RemoveAt(_choicePath.Count - 1);
		}

		if (!hasCandidate)
		{
			_firstFailure ??= new ModDependencyUnavailableException(dependent, alternatives
				.Select(x => new ModDependencyOption(FindMod(x.ModName), null, null,
					GetUnavailableReason(dependent.Name, x) ?? ModDependencyUnavailableReason.UnsatisfiableDependencies.Instance))
				.ToArray());
		}

		return false;
	}

	/// <summary>Meets the entry of <paramref name="agenda"/> by settling one of <paramref name="tried"/>, then the rest of it.</summary>
	private bool TrySettle(Agenda agenda, IEnumerable<ModDependencyAlternative> tried, ref bool hasCandidate)
	{
		var dependentName = agenda.Entry.Dependent.Name;
		foreach (var alternative in tried)
		{
			foreach (var candidate in GetCandidates(alternative))
			{
				hasCandidate = true;
				_settled.Add(alternative.ModName, candidate);
				AddWait(dependentName, alternative.ModName, candidate);
				if (Search(candidate.Install is { } install ? Push(install, agenda.Next) : agenda.Next))
				{
					return true;
				}

				RemoveWait(dependentName, candidate);
				_settled.Remove(alternative.ModName);
			}
		}

		return false;
	}

	private bool IsInstalledInRange(ModDependencyAlternative alternative) =>
		!_settled.ContainsKey(alternative.ModName) && _installedMods.TryGetValue(alternative.ModName, out var installed) &&
		installed.IsFinished && alternative.VersionRange.Satisfies(installed.Version);

	/// <summary>
	/// How an alternative whose mod is not settled yet can be settled, preferred first: keeping the installed version in
	/// range, then the reachable versions in range from the greatest.
	/// </summary>
	private IEnumerable<Settled> GetCandidates(ModDependencyAlternative alternative)
	{
		if (_settled.ContainsKey(alternative.ModName))
		{
			yield break;
		}

		var mod = FindMod(alternative.ModName);
		var installed = _installedMods.GetValueOrDefault(mod.Name);
		if (IsInstalledInRange(alternative))
		{
			yield return new Settled(installed!.Version, null);
		}

		if (installed != null && mod.FindItem(installed.Version) == null || FindOtherExclusiveMod(mod) != null)
		{
			yield break;
		}

		foreach (var (item, version) in GetReachableVersions(mod, alternative.VersionRange).OrderByDescending(x => x.Version))
		{
			if (installed == null || version > installed.Version || version == installed.Version && !installed.IsFinished)
			{
				yield return new Settled(version, new PlannedModInstall(mod, item, version, installed?.Version));
			}
		}
	}

	/// <summary>Why no candidate of the alternative exists in the current state, or <see langword="null"/> when one does.</summary>
	private ModDependencyUnavailableReason? GetUnavailableReason(string dependentName, ModDependencyAlternative alternative)
	{
		if (_settled.TryGetValue(alternative.ModName, out var settled))
		{
			if (alternative.ModName == _requestedModName ||
			    alternative.VersionRange.Satisfies(settled.Version) && WouldCycle(dependentName, alternative.ModName, settled))
			{
				return ModDependencyUnavailableReason.Cycle.Instance;
			}

			return new ModDependencyUnavailableReason.ConflictingVersion(settled.Version);
		}

		if (IsInstalledInRange(alternative))
		{
			return null;
		}

		var mod = FindMod(alternative.ModName);
		var latest = GetReachableVersions(mod, alternative.VersionRange).Select(x => x.Version).Max();
		if (latest == null)
		{
			return new ModDependencyUnavailableReason.NoSupportedVersion(alternative.VersionRange);
		}

		if (_installedMods.TryGetValue(mod.Name, out var installed))
		{
			if (mod.FindItem(installed.Version) == null)
			{
				return new ModDependencyUnavailableReason.UnknownInstalledVersion(installed.Version);
			}

			if (latest < installed.Version)
			{
				return new ModDependencyUnavailableReason.NewerVersionInstalled(installed.Version);
			}
		}

		if (FindOtherExclusiveMod(mod) is { } otherMod)
		{
			return new ModDependencyUnavailableReason.OtherModInExclusiveCategory(otherMod);
		}

		return null;
	}

	/// <summary>The versions in <paramref name="range"/> reachable by a version supporting the modpack and a prefix of its patches.</summary>
	private IEnumerable<(ModFeedItem Item, NuGetVersion Version)> GetReachableVersions(ModFeedData mod, VersionRange range) =>
		mod.Items
			.Where(x => x.SupportedModpackVersion.Satisfies(_modpackVersion))
			.SelectMany(x => (x.Patches ?? []).Select(p => p.Version).Append(x.Version).Where(range.Satisfies).Select(v => (x, v)));

	/// <summary>Another mod of <paramref name="mod"/>'s exclusive category that is installed, settled or queued.</summary>
	private ModFeedData? FindOtherExclusiveMod(ModFeedData mod) =>
		_categories.Any(x => x.Id == mod.CategoryId && x.IsExclusive)
			? _mods.FirstOrDefault(x => x.CategoryId == mod.CategoryId && x.Name != mod.Name &&
			                            (_installedMods.ContainsKey(x.Name) || _settled.ContainsKey(x.Name) ||
			                             _queuedInstalls.Contains(x.Name)))
			: null;

	/// <summary>
	/// Whether <paramref name="dependentName"/> waiting for the planned <paramref name="modName"/> closes a cycle: the
	/// requested mod is installed last, and a planned mod cannot wait for one that waits for it.
	/// </summary>
	private bool WouldCycle(string dependentName, string modName, Settled settled) =>
		modName == _requestedModName || settled.Install != null && Reaches(modName, dependentName);

	private bool Reaches(string from, string to) =>
		from == to || _waitsFor.TryGetValue(from, out var next) && next.Any(x => Reaches(x, to));

	private void AddWait(string dependentName, string modName, Settled settled)
	{
		if (settled.Install == null)
		{
			return;
		}

		if (!_waitsFor.TryGetValue(dependentName, out var list))
		{
			_waitsFor.Add(dependentName, list = []);
		}

		list.Add(modName);
	}

	private void RemoveWait(string dependentName, Settled settled)
	{
		if (settled.Install != null)
		{
			var list = _waitsFor[dependentName];
			list.RemoveAt(list.Count - 1);
		}
	}

	private ModFeedData FindMod(string name) =>
		_mods.FirstOrDefault(x => x.Name == name) ?? throw new InvalidOperationException($"Dependency {name} is not a feed mod");

	/// <summary>
	/// The planned installations of the found plan, each after those it waits for, after checking that the final versions
	/// meet every entry of the requested and planned versions.
	/// </summary>
	private Solution CreateSolution()
	{
		var installs = _settled
			.Where(x => x.Key != _requestedModName && x.Value.Install != null)
			.ToDictionary(x => x.Key, x => x.Value.Install!, StringComparer.Ordinal);
		foreach (var install in installs.Values.Append(_settled[_requestedModName].Install!))
		{
			foreach (var dependency in install.Item.Dependencies ?? [])
			{
				var alternatives = dependency switch
				{
					ModDependency.AnotherMod(var mods) => mods,
				};
				if (!alternatives.Any(x => _settled.TryGetValue(x.ModName, out var s) && x.VersionRange.Satisfies(s.Version)))
				{
					throw new InvalidOperationException($"The plan leaves a dependency of mod {install.Mod.Name} unmet.");
				}
			}
		}

		var plan = new List<PlannedModInstall>(installs.Count);
		var added = new HashSet<string>(StringComparer.Ordinal);
		AddAfterDependencies(_requestedModName);
		var kept = _settled
			.Where(x => x.Key != _requestedModName && x.Value.Install == null)
			.ToDictionary(x => x.Key, x => x.Value.Version, StringComparer.Ordinal);
		return new Solution(plan, installs, kept, _choicePath.Count > 0 ? _choicePath[0] : null);

		void AddAfterDependencies(string modName)
		{
			foreach (var dependency in _waitsFor.GetValueOrDefault(modName) ?? [])
			{
				if (added.Add(dependency))
				{
					AddAfterDependencies(dependency);
					plan.Add(installs[dependency]);
				}
			}
		}
	}

	private static Agenda? Push(PlannedModInstall install, Agenda? next)
	{
		var count = install.Item.Dependencies?.Count ?? 0;
		for (var i = count - 1; i >= 0; i--)
		{
			next = new Agenda((install.Mod, install.Item, i), next);
		}

		return next;
	}

	/// <summary>A dependency entry: the <paramref name="Index"/>th of the version <paramref name="ItemVersion"/> of a mod.</summary>
	private sealed record GroupKey(string DependentName, NuGetVersion ItemVersion, int Index);

	/// <summary>The entries still to meet, the next one first.</summary>
	private sealed record Agenda((ModFeedData Dependent, ModFeedItem Item, int Index) Entry, Agenda? Next);

	/// <summary>A settled mod's final <paramref name="Version"/>: planned with <paramref name="Install"/>, or kept installed when it is null.</summary>
	private sealed record Settled(NuGetVersion Version, PlannedModInstall? Install);

	/// <summary>An undecided entry with several alternatives and why each could not be settled where it was met.</summary>
	private sealed record ChoicePoint(GroupKey Key, ModFeedData Dependent, IReadOnlyList<ModDependencyAlternative> Alternatives,
		IReadOnlyList<ModDependencyUnavailableReason?> Reasons);

	/// <summary>A search's plan and, when it has an undecided choice, the first one's entry and options.</summary>
	private sealed record Round(IReadOnlyList<PlannedModInstall> Plan, GroupKey? Key, ModDependencyChoice? Choice);

	/// <summary>
	/// A found plan: its ordered <paramref name="Plan"/>, the planned installations and the installed versions it keeps,
	/// both by mod name, and its first undecided choice.
	/// </summary>
	private sealed record Solution(IReadOnlyList<PlannedModInstall> Plan,
		IReadOnlyDictionary<string, PlannedModInstall> Installs, IReadOnlyDictionary<string, NuGetVersion> Kept,
		ChoicePoint? FirstChoice);
}
