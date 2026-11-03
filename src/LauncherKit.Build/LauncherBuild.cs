using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

namespace LauncherKit.Build;

/// <summary>
/// Reusable NUKE publish, test, installer and editor components. Consumers provide their project names and product
/// props (<see cref="ProductPropsFile"/>); the version and installer output name are read from those props, and the
/// installer is built from them by the kit's WiX projects (kit/installer). No fallback product identity is supplied.
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

	/// <summary>
	/// The consumer's product props (kit/msbuild/LauncherKit.Product.props), with literal <c>VersionPrefix</c> and
	/// <c>LauncherInstallerOutputName</c> values.
	/// </summary>
	protected abstract AbsolutePath ProductPropsFile { get; }

	/// <summary>The LauncherKit checkout.</summary>
	protected virtual AbsolutePath KitDirectory => RootDirectory / "kit";

	protected string VersionPrefix => ProductProperty("VersionPrefix");

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

	/// <summary>
	/// Builds the launcher's per-user MSI (the consumer's LauncherInstallerProject or the kit's default) and the Burn
	/// bundle chaining it from the launcher publish output, and copies them to <c>out/{name}_{version}.msi</c> and
	/// <c>out/{name}_{version}.exe</c> (the setup the feed and the releases distribute), where {name} is the product's
	/// <c>LauncherInstallerOutputName</c>.
	/// </summary>
	public Target PublishInstaller => _ => _
		.DependsOn(CleanLauncherSymbols)
		.Executes(() =>
		{
			var outputName = ProductProperty("LauncherInstallerOutputName");
			var installerOutputDirectory = PublishDirectory / "Installer";
			DotNetBuild(settings => settings
				.SetProjectFile(KitDirectory / "installer" / "Bundle" / "LauncherKit.Installer.Bundle.wixproj")
				.SetConfiguration(Configuration)
				// WiX's up-to-date check does not see a changed publish output, so a stale package could be reused.
				.EnableNoIncremental()
				.SetVersionSuffix(VersionSuffix)
				.SetProperty("LauncherProductProps", ProductPropsFile)
				.SetProperty("LauncherPublishDirectory", LauncherPublishDirectory)
				.SetProperty("OutDir", installerOutputDirectory + Path.DirectorySeparatorChar));
			(installerOutputDirectory / $"{outputName}.msi").Copy(
				ArtifactsDirectory / $"{outputName}_{Version}.msi", ExistsPolicy.FileOverwrite);
			(installerOutputDirectory / $"{outputName}.exe").Copy(
				ArtifactsDirectory / $"{outputName}_{Version}.exe", ExistsPolicy.FileOverwrite);
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

	protected string ProductProperty(string name) =>
		XDocument.Load(ProductPropsFile).Descendants(name).Single().Value.Trim();

	private static string GetExecutableName(string name) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;
}
