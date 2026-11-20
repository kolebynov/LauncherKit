using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Models;

namespace SampleLauncher.App;

/// <summary>The sample's one real download: a small file from the mock server, hash-checked by the kit.</summary>
public static class SampleDownload
{
	public const string TaskId = "SampleDownload";

	// scripts/make_mocks.py generates the file and checks that this hash matches it.
	public static readonly DownloadInfo File = new()
	{
		S3Path = "Sample/sample-download.txt",
		Hash = new Sha256Hash("CA8925D34DA659EB19EC028FB9A3E60240C563C5F459A72CBD2C8EA17BD9ED24"),
	};

	/// <summary>A queue task downloading <see cref="File"/> into the launcher's Downloads storage.</summary>
	public static AppTask CreateTask() => new(
		TaskId, new AppTaskDescription.Custom("Download sample file"),
		context => [context.MicroTasksFactory.CreateDownloadFileMicroTask(File)],
		[], null);
}
