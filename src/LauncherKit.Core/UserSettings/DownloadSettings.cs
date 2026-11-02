namespace LauncherKit.Core.UserSettings;

public sealed class DownloadSettings
{
	/// <summary>How long a finished download is kept after it was finished.</summary>
	public FileStoragePeriod FileStoragePeriod { get; set; } = FileStoragePeriod.Week;

	/// <summary>How long a partial download is kept after its last write.</summary>
	public FileStoragePeriod PartialFileStoragePeriod { get; set; } = FileStoragePeriod.Day;

	/// <summary>Global download cap in bytes per second; 0 (or negative) means unlimited.</summary>
	public long MaxDownloadSpeedBytesPerSecond { get; set; }
}