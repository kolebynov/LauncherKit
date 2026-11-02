namespace LauncherKit.Core.Backups;

/// <summary>What a <c>mergePlugins</c> action needs to be undone.</summary>
/// <param name="Profile">The Mod Organizer profile the plugins were merged into.</param>
/// <param name="PreviousLines">The profile's <c>plugins.txt</c> before the merge.</param>
/// <param name="MergedLines">The merged <c>plugins.txt</c>.</param>
public sealed record PluginsMergeBackup(string Profile, IReadOnlyList<string> PreviousLines,
	IReadOnlyList<string> MergedLines);
