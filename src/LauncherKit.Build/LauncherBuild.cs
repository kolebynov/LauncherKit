using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

namespace LauncherKit.Build;

/// <summary>
/// Reusable NUKE publish, test and editor components. Consumers provide all project and product names and their own
/// installer packaging; this project neither imports consumer product properties nor supplies a fallback product
/// identity.
/// </summary>
public abstract class LauncherBuild : NukeBuild
{
	[Parameter("Configuration to build - Default is Release")]
	protected readonly string Configuration = "Release";

	[Parameter]
	protected readonly AbsolutePath ArtifactsDirectory = RootDirectory / "out";

	[Parameter("Version suffix appended to the launcher version, e.g. 'beta.42' - Default is none (stable)")]
	protected readonly string? VersionSuffix;

	[Solution]
	protected readonly Solution Solution = null!;

	protected abstract string LauncherProjectName { get; }
	protected abstract string CollectorProjectName { get; }
	protected abstract string CollectorExecutableName { get; }
	protected abstract string FeedEditorProjectName { get; }
	protected abstract string VersionPrefix { get; }

	protected static string Runtime => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
	protected string Version => string.IsNullOrWhiteSpace(VersionSuffix)
		? VersionPrefix
		: $"{VersionPrefix}-{VersionSuffix}";
	protected AbsolutePath PublishDirectory => ArtifactsDirectory / "publish";
	protected AbsolutePath LauncherPublishDirectory => PublishDirectory / "Launcher";

	public Target Clean => _ => _
		.Executes(() => ArtifactsDirectory.DeleteDirectory());

	public Target Test => _ => _
		.Executes(() =>
		{
			DotNetTest(settings => settings
				.SetProjectFile(Solution.Path)
				.SetConfiguration(Configuration)
				.EnableNoLogo()
				.SetLoggers("trx")
				.SetResultsDirectory(ArtifactsDirectory / "test-results")
				.SetDataCollector("XPlat Code Coverage"));
		});

	public Target PublishLauncher => _ => _
		.DependsOn(Clean)
		.Executes(() =>
		{
			DotNetPublish(settings => settings
				.SetProject(GetProjectPath(LauncherProjectName))
				.SetConfiguration(Configuration)
				.SetRuntime(Runtime)
				.SetOutput(LauncherPublishDirectory)
				.SetVersionSuffix(VersionSuffix)
				.SetSelfContained(true)
				.SetProperty("PublishAot", true)
				// A global property prevents environment variables from enabling the development API in releases.
				.SetProperty("EnableDevAutomation", false));
		});

	public Target CleanLauncherSymbols => _ => _
		.DependsOn(PublishLauncher)
		.Executes(() =>
		{
			// Neither native symbols nor the project-reference and graphics-library symbols ship with the launcher.
			foreach (var symbolFilePath in LauncherPublishDirectory.GlobFiles("*.pdb"))
			{
				symbolFilePath.DeleteFile();
			}
		});

	public Target PublishConsoleCollector => _ => _
		.DependsOn(Clean)
		.Executes(() =>
		{
			var collectorPublishDirectory = PublishDirectory / "LogsCollector";
			DotNetPublish(settings => settings
				.SetProject(GetProjectPath(CollectorProjectName))
				.SetConfiguration(Configuration)
				.SetRuntime(Runtime)
				.SetOutput(collectorPublishDirectory)
				.SetVersionSuffix(VersionSuffix));
			(collectorPublishDirectory / GetExecutableName(CollectorExecutableName)).CopyToDirectory(ArtifactsDirectory);
		});

	public Target PublishConsoleHelper => _ => _
		.Executes(() =>
		{
			DotNetPublish(settings => settings
				.SetProject(GetProjectPath("LauncherKit.ConsoleHelper"))
				.SetConfiguration(Configuration)
				.SetRuntime(Runtime)
				.SetOutput(PublishDirectory / "ConsoleHelper"));
		});

	/// <summary>
	/// Publishes a self-contained editor to out/FeedEditor-{runtime}. Blazor and MudBlazor require reflection;
	/// the editor is not trimmed and remains separate from the consumer's default release targets.
	/// </summary>
	public Target PublishFeedEditor => _ => _
		.DependsOn(Clean)
		.Executes(() =>
		{
			DotNetPublish(settings => settings
				.SetProject(GetProjectPath(FeedEditorProjectName))
				.SetConfiguration(Configuration)
				.SetRuntime(Runtime)
				.SetOutput(ArtifactsDirectory / $"FeedEditor-{Runtime}")
				.SetVersionSuffix(VersionSuffix)
				.SetSelfContained(true));
		});

	protected AbsolutePath GetProjectPath(string projectName) =>
		Solution.AllProjects.First(project => project.Name == projectName).Path;

	private static string GetExecutableName(string name) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;
}
