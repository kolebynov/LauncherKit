using System.Formats.Tar;
using System.IO.Compression;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

namespace LauncherKit.Build;

/// <summary>
/// Reusable NUKE publish, packaging, test and editor components. Consumers provide all project and product names;
/// this project neither imports consumer product properties nor supplies a fallback product identity.
/// </summary>
public abstract class LauncherBuild : NukeBuild
{
	[Parameter("Configuration to build - Default is Release")]
	protected readonly string Configuration = "Release";

	[Parameter]
	protected readonly AbsolutePath ArtifactsDirectory = RootDirectory / "out";

	[Parameter("Version suffix appended to the launcher version, e.g. 'beta.42' - Default is none (stable)")]
	protected readonly string? VersionSuffix;

	[Parameter("Release asset name format, without extension; defaults to {AssetName}_{version}")]
	protected readonly string? ReleaseAssetNameFormat;

	[Solution]
	protected readonly Solution Solution = null!;

	protected abstract string LauncherProjectName { get; }
	protected abstract string BootstrapperProjectName { get; }
	protected abstract string BootstrapperExecutableName { get; }
	protected abstract string CollectorProjectName { get; }
	protected abstract string CollectorExecutableName { get; }
	protected abstract string ConsoleHelperProjectName { get; }
	protected abstract string FeedEditorProjectName { get; }
	protected abstract string AssetName { get; }
	protected abstract string VersionPrefix { get; }

	protected static string Runtime => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
	protected string Version => string.IsNullOrWhiteSpace(VersionSuffix)
		? VersionPrefix
		: $"{VersionPrefix}-{VersionSuffix}";
	protected AbsolutePath PublishDirectory => ArtifactsDirectory / "publish";
	protected AbsolutePath LauncherPublishDirectory => PublishDirectory / "Launcher";
	protected AbsolutePath LauncherArchivePath => PublishDirectory / "LauncherArchive.tar.br";

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
			// Neither native symbols nor the project-reference and graphics-library symbols ship in the archive.
			foreach (var symbolFilePath in LauncherPublishDirectory.GlobFiles("*.pdb"))
			{
				symbolFilePath.DeleteFile();
			}
		});

	public Target ZipLauncher => _ => _
		.DependsOn(CleanLauncherSymbols)
		.Executes(async () =>
		{
			using var launcherArchiveStream = File.Create(LauncherArchivePath, 8192,
				FileOptions.Asynchronous | FileOptions.SequentialScan);
			using var brotliStream = new BrotliStream(launcherArchiveStream, new BrotliCompressionOptions { Quality = 9 });
			using var tarWriter = new TarWriter(brotliStream);
			foreach (var filePath in Directory.EnumerateFiles(LauncherPublishDirectory))
			{
				await tarWriter.WriteEntryAsync(filePath, Path.GetFileName(filePath));
			}
		});

	public Target PublishBootstrapper => _ => _
		.DependsOn(ZipLauncher)
		.Executes(() =>
		{
			var bootstrapperPublishDirectory = PublishDirectory / "Bootstrapper";
			DotNetPublish(settings => settings
				.SetProject(GetProjectPath(BootstrapperProjectName))
				.SetConfiguration(Configuration)
				.SetRuntime(Runtime)
				.SetOutput(bootstrapperPublishDirectory)
				.SetVersionSuffix(VersionSuffix)
				.SetProperty("LauncherArchive", LauncherArchivePath));
			var assetName = string.Format(System.Globalization.CultureInfo.InvariantCulture,
				ReleaseAssetNameFormat ?? $"{AssetName}_{{0}}", Version);
			(bootstrapperPublishDirectory / GetExecutableName(BootstrapperExecutableName)).Copy(
				ArtifactsDirectory / GetExecutableName(assetName), ExistsPolicy.FileOverwrite);
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
				.SetProject(GetProjectPath(ConsoleHelperProjectName))
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
