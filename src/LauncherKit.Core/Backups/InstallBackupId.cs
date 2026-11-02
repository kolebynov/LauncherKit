using NuGet.Versioning;

namespace LauncherKit.Core.Backups;

/// <summary>
/// Identifies the backups taken while installing feed install steps, so rolling the steps back finds them. An ID may
/// contain other IDs: the backups of a mod contain the backups of each of its versions and patches.
/// </summary>
public sealed record InstallBackupId
{
	private InstallBackupId(string relativePath)
	{
		RelativePath = relativePath;
	}

	/// <summary>The directory of the backups, relative to the backups directory.</summary>
	public string RelativePath { get; }

	/// <summary>Every backup of the mod <paramref name="modName"/>, a Mod Organizer mod directory name.</summary>
	public static InstallBackupId ForMod(string modName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modName);
		if (modName is "." or ".." || modName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			throw new ArgumentException($"Mod name {modName} is not a valid directory name", nameof(modName));
		}

		return new InstallBackupId(Path.Combine("Mods", modName));
	}

	/// <summary>The backups of the install steps of the mod version or patch <paramref name="version"/>.</summary>
	public static InstallBackupId ForModVersion(string modName, NuGetVersion version) =>
		new(Path.Combine(ForMod(modName).RelativePath, version.ToNormalizedString()));

	/// <summary>The backup of the action <paramref name="actionIndex"/> of the step <paramref name="stepIndex"/>.</summary>
	public InstallActionBackup ForAction(int stepIndex, int actionIndex) => new(this, stepIndex, actionIndex);
}
