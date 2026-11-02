namespace LauncherKit.Core.Backups;

/// <summary>
/// The backup of one install action: the action <paramref name="ActionIndex"/> of the step <paramref name="StepIndex"/>
/// of the step list whose backups are <paramref name="Id"/>.
/// </summary>
public sealed record InstallActionBackup(InstallBackupId Id, int StepIndex, int ActionIndex)
{
	/// <summary>The directory of the backup, relative to the backups directory.</summary>
	public string RelativePath => Path.Combine(Id.RelativePath, $"{StepIndex}.{ActionIndex}");
}
