namespace LauncherKit.FeedEditor.Feed;

/// <summary>
/// The launcher's <c>InstallAction</c> closed hierarchy. Each case is a sealed subclass whose <c>$type</c> is the class
/// name without the <c>Action</c> suffix; see <see cref="Json.ModInstallActionJsonConverter"/>.
/// </summary>
public abstract class ModInstallActionModel : FeedEntity;

/// <summary>
/// Downloads an additional mod archive and installs it as the mod <see cref="ModName"/>, at <see cref="Placement"/>
/// when set.
/// </summary>
public sealed class InstallModAction : ModInstallActionModel
{
	public DownloadInfoModel DownloadInfo { get; set; } = new();

	public string ModName { get; set; } = string.Empty;

	/// <summary>Where the launcher puts the mod in the mod list; <see langword="null"/> for the default placement.</summary>
	public ModPlacementModel? Placement { get; set; }
}

public sealed class SelectRequiredModAction : ModInstallActionModel
{
	public List<string> Mods { get; init; } = [];
}

/// <summary>
/// Asks the user to go through <see cref="Steps"/> in a dialog titled <see cref="Title"/>; each option's
/// <see cref="ChoiceOptionModel.Id"/> then holds <see cref="Selected"/> or <see cref="NotSelected"/> for the conditions of
/// later steps.
/// </summary>
public sealed class SelectChoicesAction : ModInstallActionModel
{
	public const string Selected = "true";

	public const string NotSelected = "false";

	public Dictionary<string, string> Title { get; init => field = value ?? []; } = [];

	public List<ChoiceStepModel> Steps { get; init => field = value ?? []; } = [];
}

/// <summary>How many options of a choice page the user must select, as the launcher's <c>ChoiceSelectionMode</c>.</summary>
public enum ChoiceSelectionMode
{
	Any,
	AtLeastOne,
	AtMostOne,
	ExactlyOne,
	All,
}

/// <summary>One page of a <see cref="SelectChoicesAction"/>.</summary>
public sealed class ChoiceStepModel : FeedEntity
{
	public Dictionary<string, string> Title { get; init => field = value ?? []; } = [];

	public IReadOnlyDictionary<string, string>? Subtitle { get; set; }

	public ChoiceSelectionMode SelectionMode { get; set; } = ChoiceSelectionMode.ExactlyOne;

	public List<ChoiceOptionModel> Options { get; init => field = value ?? []; } = [];
}

public sealed class ChoiceOptionModel : FeedEntity
{
	/// <summary>The condition key under which the launcher records whether the option was selected.</summary>
	public string Id { get; set; } = string.Empty;

	public Dictionary<string, string> Name { get; init => field = value ?? []; } = [];

	public IReadOnlyDictionary<string, string>? Description { get; set; }

	public bool IsRequired { get; set; }

	public bool IsSelectedByDefault { get; set; }
}

public sealed class ActivateModsAction : ModInstallActionModel
{
	public bool Activate { get; set; } = true;

	public List<string> Mods { get; init; } = [];
}

public sealed class ActivatePluginsAction : ModInstallActionModel
{
	public bool Activate { get; set; } = true;

	public List<string> Plugins { get; init; } = [];
}

/// <summary>
/// Deletes the files and directories <see cref="Paths"/>, relative to the Skyrim directory, in order. A path that
/// cannot be deleted fails the installation when <see cref="FailOnFailedDelete"/> is set (the launcher's default when
/// missing); otherwise it is skipped.
/// </summary>
public sealed class RemoveFileSystemItemAction : ModInstallActionModel
{
	public List<string> Paths { get; init; } = [];

	public bool FailOnFailedDelete { get; set; } = true;
}

/// <summary>
/// Downloads an archive and extracts it whole into <see cref="DestinationPath"/>, relative to the Skyrim directory
/// (empty for the Skyrim directory itself).
/// </summary>
public sealed class ExtractArchiveAction : ModInstallActionModel
{
	public DownloadInfoModel DownloadInfo { get; set; } = new();

	public string DestinationPath { get; set => field = value ?? string.Empty; } = string.Empty;
}

/// <summary>
/// Downloads a <c>plugins.txt</c>, possibly listing only some plugins, and merges it into the selected profile's
/// <c>plugins.txt</c>: its plugins get its relative order and active state (missing ones are added next to the plugin
/// preceding them), the other plugins keep their places, and <c>loadorder.txt</c> is rebuilt.
/// </summary>
public sealed class MergePluginsAction : ModInstallActionModel
{
	public DownloadInfoModel DownloadInfo { get; set; } = new();
}

public sealed class ChangeProfileAction : ModInstallActionModel
{
	public string ProfileName { get; set; } = string.Empty;
}
