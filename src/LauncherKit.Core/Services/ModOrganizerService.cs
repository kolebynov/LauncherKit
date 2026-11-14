using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IniParser;
using IniParser.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

public sealed partial class ModOrganizerService : IModOrganizerService
{
	private const string ModOrganizerAppName = "ModOrganizer";
	private const string SeparatorSuffix = "_separator";
	private static readonly string CmdPath =
		Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "System32", "cmd.exe");

	private readonly string _userModsSeparator;

	private const string SaveExtension = ".ess";
	private const string CoSaveExtension = ".skse";

	private readonly ILogger<ModOrganizerService> _logger;
	private readonly IProcessRunner _processRunner;
	private readonly FileIniDataParser _fileIniDataParser;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly ISevenZipService _sevenZipService;
	private readonly IInstallBackupService _installBackupService;
	private readonly string _modOrganizerDirectory;
	private readonly string _iniPath;
	private readonly string _profilesDirectory;
	private readonly string _skyrimDirectory;
	private readonly string _moExePath;

	public string ModsDirectory { get; }

	public IEnumerable<ModInfo> InstalledMods => Directory.EnumerateDirectories(ModsDirectory)
		.Where(path => !path.EndsWith(SeparatorSuffix, StringComparison.Ordinal))
		.Select(path =>
		{
			var metaIniLazy = new Lazy<MetaIniData?>(() =>
			{
				var metaIniPath = Path.Combine(path, "meta.ini");
				if (!File.Exists(metaIniPath))
				{
					return null;
				}

				var metaIniData = _fileIniDataParser.ReadFile(metaIniPath, Encoding.UTF8);
				return new MetaIniData
				{
					Comments = metaIniData["General"]["comments"],
					Version = metaIniData["General"]["version"],
				};
			});

			return new ModInfo(Path.GetFileName(path), metaIniLazy);
		});

	public ModOrganizerService(IOptionsSnapshot<AppSettings> appSettings, ILogger<ModOrganizerService> logger,
		IProcessRunner processRunner, FileIniDataParser fileIniDataParser, IFileSystemUtils fileSystemUtils,
		ISevenZipService sevenZipService, IInstallBackupService installBackupService, IOptions<LauncherProductOptions> product)
	{
		_logger = logger;
		_processRunner = processRunner;
		_fileIniDataParser = fileIniDataParser;
		_fileSystemUtils = fileSystemUtils;
		_sevenZipService = sevenZipService;
		_installBackupService = installBackupService;
		_userModsSeparator = product.Value.UserModsSeparator;
		_modOrganizerDirectory = appSettings.Value.GetModOrganizerDirectory(product.Value) ??
		                         throw new InvalidOperationException("Can't initialize ModOrganizerService without ModOrganizer directory");
		_skyrimDirectory = appSettings.Value.SkyrimDirectory!;
		_iniPath = Path.Combine(_modOrganizerDirectory, "ModOrganizer.ini");
		_profilesDirectory = Path.Combine(_modOrganizerDirectory, "profiles");
		_moExePath = Path.Combine(_modOrganizerDirectory, $"{ModOrganizerAppName}.exe");
		ModsDirectory = Path.Combine(_modOrganizerDirectory, "mods");
	}

	public async Task InstallMod(string modName, string archivePath, ModPlacement? placement,
		CancellationToken cancellationToken)
	{
		if (IsModInstalled(modName))
		{
			throw new ArgumentException($"Mod {modName} is already installed", nameof(modName));
		}

		_logger.LogInformation("Installing mod {ModName} from {ArchivePath}", modName, archivePath);
		await _sevenZipService.ExtractArchive(archivePath, Path.Combine(ModsDirectory, modName), cancellationToken);
		if (placement != null)
		{
			await PlaceMod(modName, placement, cancellationToken);
		}

		await ActivateMod(modName, true, true, cancellationToken);
	}

	public Task PlaceMod(string modName, ModPlacement placement, CancellationToken cancellationToken) =>
		placement switch
		{
			ModPlacement.UnderSeparator(var separator) => MoveModToSeparator(modName, separator, cancellationToken),
			ModPlacement.NextToMod(var neighborName, var side) =>
				MoveModNextTo(modName, neighborName, side, cancellationToken),
		};

	private async Task MoveModToSeparator(string modName, string separator, CancellationToken cancellationToken)
	{
		var separatorName = separator + SeparatorSuffix;
		var separatorDirectory = Path.Combine(ModsDirectory, separatorName);
		var separatorMetaIniPath = Path.Combine(separatorDirectory, "meta.ini");
		if (!File.Exists(separatorMetaIniPath))
		{
			_logger.LogInformation("Creating separator {Separator}", separator);
			Directory.CreateDirectory(separatorDirectory);
			await File.WriteAllTextAsync(separatorMetaIniPath, $"[General]\r\nmodid=0\r\ngameName={LauncherConstants.ModOrganizerGameName}\r\n",
				cancellationToken);
		}

		_logger.LogInformation("Moving mod {ModName} under separator {Separator}", modName, separator);
		var profile = await GetSelectedProfile(cancellationToken);
		await _fileSystemUtils.ModifyFileLines(
			Path.Combine(_profilesDirectory, profile, "modlist.txt"),
			lines => PlaceModUnderSeparator(lines, modName, separatorName),
			cancellationToken);
	}

	private async Task MoveModNextTo(string modName, string neighborName, ModListSide side,
		CancellationToken cancellationToken)
	{
		_logger.LogInformation("Moving mod {ModName} {Side} mod {NeighborName}", modName, side.ToStringFast(),
			neighborName);
		var profile = await GetSelectedProfile(cancellationToken);
		var isNeighborListed = true;
		await _fileSystemUtils.ModifyFileLines(
			Path.Combine(_profilesDirectory, profile, "modlist.txt"),
			lines =>
			{
				var modList = PlaceModNextTo(lines, modName, neighborName, side);
				isNeighborListed = modList != null;
				return (IEnumerable<string>?)modList ?? lines;
			},
			cancellationToken);
		if (!isNeighborListed)
		{
			_logger.LogWarning("Mod {NeighborName} is not in the mod list, mod {ModName} keeps its place",
				neighborName, modName);
		}
	}

	public async Task UninstallMod(string modName, CancellationToken cancellationToken)
	{
		if (!IsModInstalled(modName))
		{
			throw new ArgumentException($"Mod {modName} is not installed", nameof(modName));
		}

		_logger.LogInformation("Uninstalling mod {ModName}", modName);
		if (_fileSystemUtils.SafeDeleteDirectory(Path.Combine(ModsDirectory, modName))
			is FileSystemUtils.SafeDeleteResult.Error(var exc))
		{
			throw new ModDeletionException(modName, exc);
		}

		_logger.LogInformation("Running ModOrganizer to update modlist and plugins");
		await OneShotStartModOrganizer(cancellationToken);
	}

	public async Task ChangeProfile(string newProfile, CancellationToken cancellationToken)
	{
		var availableProfiles = Directory.EnumerateDirectories(_profilesDirectory)
			.Select(Path.GetFileName)
			.ToArray();
		if (!availableProfiles.Contains(newProfile))
		{
			throw new ArgumentException($"Profile {newProfile} was not found", nameof(newProfile));
		}

		_logger.LogInformation("Changing selected profile in ModOrganizer.ini to {NewProfile}", newProfile);
		await _fileSystemUtils.ModifyFileContent(
			_iniPath,
			content => SelectedProfileRegex().Replace(content, $"selected_profile=@ByteArray({newProfile})"),
			cancellationToken);
	}

	public async Task ActivateMod(string modName, bool activate, bool activatePlugins, CancellationToken cancellationToken)
	{
		if (!IsModInstalled(modName))
		{
			throw new ArgumentException($"Mod {modName} was not found", nameof(modName));
		}

		_logger.LogInformation(
			"{Operation} {ModName}, activate plugins: {ActivatePlugins}", activate ? "Activating" : "Deactivating",
			modName, activatePlugins);

		var profile = await GetSelectedProfile(cancellationToken);
		await _fileSystemUtils.ModifyFileLines(
			Path.Combine(_profilesDirectory, profile, "modlist.txt"),
			lines =>
			{
				var activateChar = activate ? '+' : '-';
				var existingModIndex = lines.IndexOfAny($"-{modName}", $"+{modName}");
				IEnumerable<string> result;
				if (existingModIndex >= 0)
				{
					lines[existingModIndex] = $"{activateChar}{modName}";
					result = lines;
				}
				else
				{
					result = lines.Prepend($"{activateChar}{modName}");
				}

				return result;
			}, cancellationToken);

		_logger.LogInformation("Running ModOrganizer to update plugins");
		await OneShotStartModOrganizer(cancellationToken);

		if (activate && activatePlugins)
		{
			await ActivatePlugins(GetModPlugins(modName).ToArray(), true, cancellationToken);
		}
	}

	public bool IsModInstalled(string modName)
	{
		var modPath = Path.Combine(ModsDirectory, modName);
		return Directory.Exists(modPath) && Directory.GetFileSystemEntries(modPath).Length > 0;
	}

	public async Task<IEnumerable<ModListItem>> GetModList(CancellationToken cancellationToken)
	{
		var profile = await GetSelectedProfile(cancellationToken);
		var lines = await File.ReadAllLinesAsync(Path.Combine(_profilesDirectory, profile, "modlist.txt"),
			cancellationToken);
		return lines
			.Select(line => (line.StartsWith('+', StringComparison.Ordinal), line.StartsWith('-', StringComparison.Ordinal)) switch
			{
				(true, _) => new ModListItem(line[1..], true),
				(_, true) => new ModListItem(line[1..], false),
				_ => default
			})
			.Where(x => x != default);
	}

	public async Task ActivatePlugins(IReadOnlyList<string> pluginNames, bool activate, CancellationToken cancellationToken)
	{
		var profile = await GetSelectedProfile(cancellationToken);

		_logger.LogInformation("{Operation} plugins: {Plugins}", activate ? "Activating" : "Deactivating",
			string.Join(", ", pluginNames));
		await _fileSystemUtils.ModifyFileLines(
			Path.Combine(_profilesDirectory, profile, "plugins.txt"),
			lines =>
			{
				var activateChar = activate ? "*" : string.Empty;
				foreach (var plugin in pluginNames)
				{
					var pluginIndex = lines.IndexOfAny(plugin, $"*{plugin}");
					if (pluginIndex < 0)
					{
						_logger.LogWarning("Plugin {Plugin} was not found in plugins.txt", plugin);
						continue;
					}

					lines[pluginIndex] = $"{activateChar}{plugin}";
				}

				return lines;
			},
			cancellationToken);
	}

	public async Task MergePlugins(string pluginsFilePath, InstallActionBackup? backup, CancellationToken cancellationToken)
	{
		var profile = await GetSelectedProfile(cancellationToken);
		_logger.LogInformation("Merging plugins from {PluginsFilePath} into profile {Profile}", pluginsFilePath, profile);
		var mergedLines = await File.ReadAllLinesAsync(pluginsFilePath, cancellationToken);
		var pluginsPath = GetPluginsPath(profile);
		var currentLines = await _fileSystemUtils.ReadAllLines(pluginsPath, cancellationToken);
		var result = PluginListMerger.MergePlugins(currentLines, mergedLines);
		if (backup != null)
		{
			await _installBackupService.BackupPluginsBeforeMerge(backup,
				new PluginsMergeBackup(profile, currentLines, mergedLines), cancellationToken);
		}

		await _fileSystemUtils.WriteAllLines(pluginsPath, result.Lines, cancellationToken);
		_logger.LogInformation(
			"Merged plugins: {AddedCount} added, {MovedCount} moved, {ChangedStateCount} changed their state",
			result.AddedCount, result.MovedCount, result.ChangedStateCount);

		await RebuildLoadOrder(profile, currentLines, result.Lines, cancellationToken);
	}

	public async Task<bool> RevertPluginsMerge(InstallActionBackup backup, CancellationToken cancellationToken)
	{
		var pluginsBackup = await _installBackupService.ReadPluginsMergeBackup(backup, cancellationToken);
		if (pluginsBackup == null)
		{
			return false;
		}

		var pluginsPath = GetPluginsPath(pluginsBackup.Profile);
		if (_fileSystemUtils.FileExists(pluginsPath))
		{
			_logger.LogInformation("Reverting the plugins merge {Backup} in profile {Profile}", backup.RelativePath,
				pluginsBackup.Profile);
			var currentLines = await _fileSystemUtils.ReadAllLines(pluginsPath, cancellationToken);
			var lines = PluginListMerger.RevertMerge(currentLines, pluginsBackup.MergedLines, pluginsBackup.PreviousLines);
			await _fileSystemUtils.WriteAllLines(pluginsPath, lines, cancellationToken);
			await RebuildLoadOrder(pluginsBackup.Profile, currentLines, lines, cancellationToken);
		}
		else
		{
			_logger.LogWarning("Profile {Profile} has no plugins.txt, skipping reverting the plugins merge {Backup}",
				pluginsBackup.Profile, backup.RelativePath);
		}

		_installBackupService.Delete(backup);
		return true;
	}

	private string GetPluginsPath(string profile) => Path.Combine(_profilesDirectory, profile, "plugins.txt");

	private async Task RebuildLoadOrder(string profile, IReadOnlyList<string> previousPluginsLines,
		IReadOnlyList<string> pluginsLines, CancellationToken cancellationToken)
	{
		var loadOrderPath = Path.Combine(_profilesDirectory, profile, "loadorder.txt");
		var loadOrderLines = File.Exists(loadOrderPath)
			? await File.ReadAllLinesAsync(loadOrderPath, cancellationToken)
			: [];
		await File.WriteAllLinesAsync(loadOrderPath,
			PluginListMerger.BuildLoadOrder(loadOrderLines, previousPluginsLines, pluginsLines), cancellationToken);
	}

	public async Task CreateOrUpdateMetaIniFile(string modName, string? installationFile, NuGetVersion version, string? comments,
		CancellationToken cancellationToken)
	{
		if (!IsModInstalled(modName))
		{
			return;
		}

		var metaIniPath = Path.Combine(ModsDirectory, modName, "meta.ini");
		var metaIniData = File.Exists(metaIniPath) ? _fileIniDataParser.ReadFile(metaIniPath, Encoding.UTF8) : new IniData();
		metaIniData.Merge(new IniData
		{
			["General"] =
			{
				{ "version", version.ToNormalizedString() },
				{ "gameName", LauncherConstants.ModOrganizerGameName },
			},
			["installedFiles"] = { { "size", "0" } },
		});

		// An archive no longer in storage (e.g. removed by the cleanup before a patch-only update) keeps the recorded one.
		if (installationFile != null)
		{
			metaIniData["General"]["installationFile"] = installationFile.Replace('\\', '/');
		}

		// Non-empty comments are kept: they may have been written by the user in Mod Organizer.
		if (string.IsNullOrWhiteSpace(metaIniData["General"]["comments"]))
		{
			metaIniData["General"]["comments"] = comments;
		}

		await File.WriteAllTextAsync(metaIniPath, metaIniData.ToString(), cancellationToken);
	}

	public Task StartModOrganizer(SkyrimRelativePath? programPath, string? programArguments,
		IReadOnlyDictionary<string, string>? environment, bool assertSuccess, CancellationToken cancellationToken)
	{
		var absoluteProgramPath = programPath?.ToAbsolute(_skyrimDirectory);
		return StartModOrganizerInternal(absoluteProgramPath, programArguments, environment, assertSuccess, cancellationToken);
	}

	public Task OneShotStartModOrganizer(CancellationToken cancellationToken) =>
		StartModOrganizerInternal(CmdPath, "/c \"echo Hello\"", null, true, cancellationToken);

	public async Task<DeletedSavesInfo> DeleteOldSaves(int savesToKeep, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(savesToKeep);

		var profile = await GetSelectedProfile(cancellationToken);
		var profileDirectory = Path.Combine(_profilesDirectory, profile);
		var settingsPath = Path.Combine(profileDirectory, "settings.ini");
		// Without local saves the game writes into the shared Documents directory, not into the profile.
		var localSaves = File.Exists(settingsPath) &&
		                 bool.TryParse(_fileIniDataParser.ReadFile(settingsPath, Encoding.UTF8)["General"]["LocalSaves"],
			                 out var value) && value;
		if (!localSaves)
		{
			throw new LocalSavesDisabledException(profile);
		}

		var savesDirectory = new DirectoryInfo(Path.Combine(profileDirectory, "saves"));
		if (!savesDirectory.Exists)
		{
			_logger.LogInformation("Profile {Profile} has no saves directory", profile);
			return default;
		}

		var files = savesDirectory.GetFiles("*", new EnumerationOptions { MatchType = MatchType.Simple });
		var saves = files
			.Where(x => x.Extension.Equals(SaveExtension, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(x => x.LastWriteTimeUtc)
			.ThenByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var coSaves = files
			.Where(x => x.Extension.Equals(CoSaveExtension, StringComparison.OrdinalIgnoreCase))
			.ToDictionary(x => Path.GetFileNameWithoutExtension(x.Name), StringComparer.OrdinalIgnoreCase);

		// Every file written before the oldest kept save (its game file or co-save, whichever is older) is old,
		// including co-saves whose game file is missing or named differently.
		DateTime threshold;
		if (savesToKeep == 0)
		{
			threshold = DateTime.MaxValue;
		}
		else if (saves.Length == 0)
		{
			_logger.LogInformation("Profile {Profile} has no saves", profile);
			return default;
		}
		else
		{
			var lastKept = saves[Math.Min(savesToKeep, saves.Length) - 1];
			threshold = coSaves.TryGetValue(Path.GetFileNameWithoutExtension(lastKept.Name), out var lastKeptCoSave) &&
			            lastKeptCoSave.LastWriteTimeUtc < lastKept.LastWriteTimeUtc
				? lastKeptCoSave.LastWriteTimeUtc
				: lastKept.LastWriteTimeUtc;
		}

		var oldSaves = saves.Where(x => x.LastWriteTimeUtc < threshold).ToArray();
		var saveNames = saves
			.Select(x => Path.GetFileNameWithoutExtension(x.Name))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		// A co-save of an existing game file goes only with it, never on its own time.
		var orphanCoSaves = coSaves
			.Where(x => !saveNames.Contains(x.Key) && x.Value.LastWriteTimeUtc < threshold)
			.Select(x => x.Value)
			.OrderByDescending(x => x.LastWriteTimeUtc)
			.ToArray();

		_logger.LogInformation(
			"Deleting {Count} old saves and {OrphanCount} orphan co-saves of profile {Profile} written before {Threshold}, keeping {SavesToKeep}",
			oldSaves.Length, orphanCoSaves.Length, profile, threshold, savesToKeep);
		var deletedCount = 0;
		var freedBytes = 0L;
		try
		{
			foreach (var save in oldSaves)
			{
				cancellationToken.ThrowIfCancellationRequested();
				// The game file goes first: if the co-save cannot be deleted, it is left without a save and deleted
				// as an orphan next time, whereas a save without its co-save would load with the SKSE mods' data lost.
				DeleteSaveFile(save.FullName);
				deletedCount++;
				freedBytes += save.Length;
				if (coSaves.TryGetValue(Path.GetFileNameWithoutExtension(save.Name), out var coSave))
				{
					DeleteSaveFile(coSave.FullName);
					freedBytes += coSave.Length;
				}
			}

			foreach (var coSave in orphanCoSaves)
			{
				cancellationToken.ThrowIfCancellationRequested();
				DeleteSaveFile(coSave.FullName);
				freedBytes += coSave.Length;
			}
		}
		catch (Exception e)
		{
			// Reported to the caller instead of thrown: the saves deleted before the failure are gone anyway.
			_logger.LogWarning(e, "Deleting old saves stopped after {Count} saves", deletedCount);
			return new DeletedSavesInfo(deletedCount, freedBytes, e);
		}

		return new DeletedSavesInfo(deletedCount, freedBytes, null);
	}

	private async Task<string> GetSelectedProfile(CancellationToken cancellationToken)
	{
		var iniContent = await File.ReadAllTextAsync(_iniPath, cancellationToken);
		return SelectedProfileRegex().Match(iniContent).Groups[1].Value;
	}

	private IEnumerable<string> GetModPlugins(string modName)
	{
		var modPath = Path.Combine(ModsDirectory, modName);
		var enumOptions = new EnumerationOptions { MatchType = MatchType.Simple };
		return Directory.EnumerateFiles(modPath, "*.esp", enumOptions)
			.Concat(Directory.EnumerateFiles(modPath, "*.esm", enumOptions))
			.Select(x => Path.GetFileName(x)!);
	}

	private void DeleteSaveFile(string path)
	{
		if (_fileSystemUtils.SafeDeleteFile(path) is FileSystemUtils.SafeDeleteResult.Error(var exc))
		{
			throw new SaveDeletionException(Path.GetFileName(path), exc);
		}
	}

	private Task StartModOrganizerInternal(string? programPath, string? programArguments,
		IReadOnlyDictionary<string, string>? environment, bool assertSuccess, CancellationToken cancellationToken)
	{
		var isAlreadyRunning = Process.GetProcessesByName(ModOrganizerAppName).Length > 0;
		if (isAlreadyRunning)
		{
			throw new ModOrganizerAlreadyRunningException();
		}

		var arguments = (string.IsNullOrEmpty(programPath), string.IsNullOrEmpty(programArguments)) switch
		{
			(false, false) => $"\"{programPath}\" {programArguments}",
			(false, _) => $"\"{programPath}\"",
			_ => string.Empty,
		};

		// An elevated process gets a fresh environment rather than the launcher's, so the variables are set by an
		// elevated cmd.exe, which runs Mod Organizer, waits for it and exits with its exit code.
		var startInfo = environment is { Count: > 0 }
			? new ProcessStartInfo(CmdPath, CreateCommandWithEnvironment(environment, arguments))
			{
				WindowStyle = ProcessWindowStyle.Hidden,
			}
			: new ProcessStartInfo(_moExePath, arguments);
		startInfo.UseShellExecute = true;
		startInfo.Verb = "runas";
		startInfo.WorkingDirectory = _modOrganizerDirectory;
		return _processRunner.StartProcessAndAssertSuccess(startInfo, assertSuccess, cancellationToken);
	}

	/// <summary>
	/// The cmd.exe arguments that set <paramref name="environment"/> and run Mod Organizer with
	/// <paramref name="arguments"/> in its directory (an elevated cmd.exe starts in System32 whatever the working
	/// directory): <c>/D</c> skips AutoRun commands and <c>/V:OFF</c> keeps <c>!</c> literal. The command starts with
	/// <c>cd</c>, not a quote, so <c>/C</c> keeps every quote of it.
	/// </summary>
	private string CreateCommandWithEnvironment(IReadOnlyDictionary<string, string> environment, string arguments)
	{
		var command = new StringBuilder("/D /V:OFF /C ");
		command.Append(CultureInfo.InvariantCulture, $"cd /D \"{_modOrganizerDirectory}\"&& ");
		foreach (var (name, value) in environment)
		{
			if (name.Length == 0 || name.Contains('=', StringComparison.Ordinal) || !IsCmdQuotable(name) || !IsCmdQuotable(value))
			{
				throw new ArgumentException($"Environment variable {name} cannot be passed to Mod Organizer", nameof(environment));
			}

			command.Append(CultureInfo.InvariantCulture, $"set \"{name}={value}\"&& ");
		}

		command.Append(CultureInfo.InvariantCulture, $"\"{_moExePath}\"");
		if (arguments.Length > 0)
		{
			command.Append(' ').Append(arguments);
		}

		return command.ToString();
	}

	/// <summary>Whether cmd.exe reads <paramref name="text"/> in quotes literally.</summary>
	private static bool IsCmdQuotable(string text) => text.AsSpan().IndexOfAny("\"%\r\n\0") < 0;

	/// <summary>
	/// Mod Organizer shows <c>modlist.txt</c> bottom-up, so the mods shown under a separator are the lines above its line
	/// up to the previous separator. The mod goes right after that previous separator's line (or the header comments):
	/// the highest priority within the group, shown last under the separator. A missing separator is shown right above
	/// the configured user-mods separator, i.e. its line goes right below that separator's line, so the user's
	/// own mods keep the highest priority; without that separator it goes at the end of the list.
	/// </summary>
	private List<string> PlaceModUnderSeparator(string[] lines, string modName, string separatorName)
	{
		var modList = lines.ToList();
		var modIndex = modList.FindIndex(line => IsModListEntry(line, modName));
		var modState = '-';
		if (modIndex >= 0)
		{
			modState = modList[modIndex][0];
			modList.RemoveAt(modIndex);
		}

		var firstEntryIndex = modList.FindIndex(line => !line.StartsWith('#', StringComparison.Ordinal));
		if (firstEntryIndex < 0)
		{
			firstEntryIndex = modList.Count;
		}

		var separatorIndex = modList.FindIndex(line => IsModListEntry(line, separatorName));
		if (separatorIndex < 0)
		{
			var userModsSeparatorIndex = modList.FindIndex(line => IsModListEntry(line, _userModsSeparator + SeparatorSuffix));
			separatorIndex = userModsSeparatorIndex >= 0 ? userModsSeparatorIndex + 1 : firstEntryIndex;
			modList.Insert(separatorIndex, $"-{separatorName}");
		}

		var groupStart = firstEntryIndex;
		for (var i = separatorIndex - 1; i >= firstEntryIndex; i--)
		{
			if (modList[i].EndsWith(SeparatorSuffix, StringComparison.Ordinal))
			{
				groupStart = i + 1;
				break;
			}
		}

		modList.Insert(groupStart, $"{modState}{modName}");
		return modList;
	}

	/// <summary>
	/// Moves the mod's line next to the neighbor's line, keeping its state (<c>-</c> for a mod not listed yet). Mod
	/// Organizer shows <c>modlist.txt</c> bottom-up, so <see cref="ModListSide.Below"/> is the line right before the
	/// neighbor's and <see cref="ModListSide.Above"/> the line right after it.
	/// </summary>
	/// <returns>The changed list, or <see langword="null"/> when the neighbor is not listed.</returns>
	private static List<string>? PlaceModNextTo(string[] lines, string modName, string neighborName, ModListSide side)
	{
		var modList = lines.ToList();
		var modIndex = modList.FindIndex(line => IsModListEntry(line, modName));
		var modState = '-';
		if (modIndex >= 0)
		{
			modState = modList[modIndex][0];
			modList.RemoveAt(modIndex);
		}

		var neighborIndex = modList.FindIndex(line => IsModListEntry(line, neighborName));
		if (neighborIndex < 0)
		{
			return null;
		}

		modList.Insert(side == ModListSide.Below ? neighborIndex : neighborIndex + 1, $"{modState}{modName}");
		return modList;
	}

	private static bool IsModListEntry(string line, string name) =>
		line.Length == name.Length + 1 && (line[0] is '+' or '-') && line.AsSpan(1).SequenceEqual(name);

	[GeneratedRegex(@"selected_profile=@ByteArray\((.+?)\)", RegexOptions.NonBacktracking)]
	private static partial Regex SelectedProfileRegex();
}