# LauncherKit

LauncherKit is the reusable core of Windows desktop launchers for Skyrim Special Edition modpacks managed by Mod Organizer 2. A consumer adds its product identity, views and branding; the kit supplies everything else:

- **Feed-driven installs and updates.** Modpacks, modpack patches, optional mods, graph packs, install components and launcher updates come from a JSON feed (`feedV11.json`) served over HTTP from a main server with a proxy fallback.
- **Task queue.** Installs run as dependency-aware task graphs with progress, resume and cancellation. Downloads are resumable and multi-part with hash checks; a file storage tracks every download.
- **Installation steps.** 7-Zip extraction and FOMOD installers, Mod Organizer profile, mod list and plugin management, and backups that let installed mods be rolled back and uninstalled.
- **Accounts and analytics.** Discord account linking (OAuth with PKCE) with role-based beta access, beta codes, and anonymous installation analytics.
- **Desktop.** Avalonia view models and the application host, with localized default texts.
- **Tools.** A logs collector, a feed editor (Blazor Server, Cloudflare R2), a per-user WiX installer and setup bundle, a Cloudflare Analytics Worker, NUKE build targets and development automation.

Contributor rules and detailed contracts are in [AGENTS.md](AGENTS.md).

## Repository layout

| Path | Contents |
|---|---|
| `src/LauncherKit.Infrastructure` | Product identity options, filesystem, registry and logging primitives. |
| `src/LauncherKit.Core` | Feed, update selection, downloads, storage, tasks, Mod Organizer, FOMOD, backups, persistence, OAuth/Discord, analytics. |
| `src/LauncherKit.Desktop` | Avalonia desktop layer: view models, typed presentation facts, default localized texts (`DesktopStrings`), the `LauncherKitApp`/`LauncherKitProgram` host, view locator, converters, controls and platform services. |
| `src/LauncherKit.LogsCollector.Core` | Logs collector engine and console runner. |
| `src/LauncherKit.ProductIdentity.Generator` | Source generator that turns the consumer's product props into a `ProductIdentity` class. |
| `src/LauncherKit.Build` | NUKE build base (`LauncherBuild`). |
| `src/LauncherKit.FeedEditor` | Feed editor web app. |
| `src/LauncherKit.DevAutomation` | Development-only UI automation API ([docs/dev-automation.md](docs/dev-automation.md)). |
| `src/LauncherKit.ConsoleHelper` | Developer console tool: beta code hashes, file SHA-256, test downloads. |
| `msbuild/LauncherKit.Product.props` | Imported by the consumer's product props; wires the identity generator. |
| `installer/` | Shared WiX MSI (`LauncherKit.Installer.props`, `Package.wxs`) and Burn setup bundle (`Bundle/`). |
| `fomod-installer/` | FOMOD script engine. |
| `workers/analytics/` | Cloudflare Analytics Worker code and tests. |
| `scripts/` | Python sandbox, automation client and feed editor development tools. |
| `tests/` | NUnit tests, one project per production project. |

## Requirements

- Windows x64 with the .NET 11 SDK (prerelease while needed). The public API uses C# preview features (`union`, `closed record`, `extension` members), so consumers set `LangVersion=preview`, which the kit's `Directory.Build.props` does.
- Desktop projects target `net11.0-windows`; the NUKE build targets `net10.0`.
- Avalonia 12.1.x, pinned in `Directory.Packages.props`.
- For NativeAOT publish: the MSVC linker and Windows SDK (Visual Studio "Desktop development with C++" workload or the Build Tools).
- For the installer: nothing to install. WiX 7 is restored from NuGet, and the projects accept the WiX 7 Open Source Maintenance Fee EULA (`AcceptEula=wix7`).
- For the Analytics Worker: Node.js and npm.

## Setting up a consumer

The kit is consumed as source: add it as a Git submodule at `kit/` and reference its projects and props by path. The NUKE build expects that path unless the consumer's `Build` overrides `KitDirectory`. No NuGet packages are published.

```powershell
git submodule add <LauncherKit repository URL> kit
git submodule update --init --recursive
```

Update the kit later with `git submodule update --remote kit` and commit the new pointer.

A consumer repository typically looks like this (names are examples):

```text
global.json
Directory.Build.props
Directory.Packages.props
MyLauncher.Product.props
MyLauncher.sln
kit/                          LauncherKit submodule
build/                        NUKE host (_build.csproj, Build.cs) + build.ps1/build.sh
src/MyLauncher.App/           launcher executable: App, views, assets, appsettings.json
src/MyLauncher.LogsCollector/ logs collector executable
src/MyLauncher.Installer/     MSI layout project
```

### 1. SDK and shared build settings

`global.json`:

```json
{
	"sdk": {
		"version": "11.0",
		"rollForward": "latestMinor",
		"allowPrerelease": true
	}
}
```

`Directory.Build.props` imports the kit's compiler policy: nullable, implicit usings, latest analyzers, warnings as errors, and the NativeAOT satellite resource fix.

```xml
<Project>
	<Import Project="kit/Directory.Build.props"/>
</Project>
```

`Directory.Packages.props` imports the kit's central package versions and adds the consumer's own packages:

```xml
<Project>
	<Import Project="kit/Directory.Packages.props"/>
	<ItemGroup>
		<PackageVersion Include="FluentAvaloniaUI" Version="3.1.0"/>
	</ItemGroup>
</Project>
```

### 2. Product props

One props file defines the product's whole identity. The launcher, the logs collector, the installer and the build all read it. The kit has no default names: a missing or blank value fails the build with `LKPI001`.

```xml
<Project>
	<PropertyGroup>
		<VersionPrefix>1.0.0</VersionPrefix>
		<LauncherProductNamespace>MyLauncher</LauncherProductNamespace>
		<LauncherSkyrimDataDirectoryName>.MyLauncher</LauncherSkyrimDataDirectoryName>
		<LauncherLocalDatabaseFileName>mylauncher.db</LauncherLocalDatabaseFileName>
		<LauncherOAuthTokenEntropy>MyLauncher.OAuthToken</LauncherOAuthTokenEntropy>
		<LauncherEnvironmentVariablePrefix>MYLAUNCHER_</LauncherEnvironmentVariablePrefix>
		<LauncherAppExecutableName>MyLauncher.App.exe</LauncherAppExecutableName>
		<LauncherDisplayName>My Launcher</LauncherDisplayName>
		<LauncherUserAgentProduct>MyLauncher</LauncherUserAgentProduct>
		<LauncherModOrganizerDirectoryName>Mod Organizer</LauncherModOrganizerDirectoryName>
		<LauncherModpackProfileName>MyModpack</LauncherModpackProfileName>
		<LauncherModpackVersionFile>Mod Organizer/mods/MyModpack/version.txt</LauncherModpackVersionFile>
		<LauncherUserModsSeparator>USER MODS</LauncherUserModsSeparator>
		<LauncherRequiredSkyrimVersion>1.5.97.0</LauncherRequiredSkyrimVersion>
		<LauncherRegistryKey>HKEY_CURRENT_USER\Software\My Team\My Launcher</LauncherRegistryKey>
	</PropertyGroup>
	<PropertyGroup>
		<LauncherManufacturer>My Team</LauncherManufacturer>
		<LauncherInstallerPackageId>MyTeam.MyLauncher</LauncherInstallerPackageId>
		<LauncherInstallerBundleId>MyTeam.MyLauncher.Bundle</LauncherInstallerBundleId>
		<LauncherInstallerOutputName>MyLauncher</LauncherInstallerOutputName>
		<LauncherIconFile>$(MSBuildThisFileDirectory)src\MyLauncher.App\Assets\favicon.ico</LauncherIconFile>
		<LauncherInstallerLogoFile>$(MSBuildThisFileDirectory)src\MyLauncher.Installer\Logo.png</LauncherInstallerLogoFile>
		<LauncherInstallerProject>$(MSBuildThisFileDirectory)src\MyLauncher.Installer\MyLauncher.Installer.wixproj</LauncherInstallerProject>
	</PropertyGroup>
	<Import Project="kit\msbuild\LauncherKit.Product.props"/>
</Project>
```

| Property | Meaning |
|---|---|
| `VersionPrefix` | Launcher version, compared with the feed's launcher versions. Keep it a literal: the build reads it as XML. NUKE `--version-suffix` adds a prerelease suffix. |
| `LauncherProductNamespace` | Namespace of the generated `ProductIdentity` class. |
| `LauncherSkyrimDataDirectoryName` | Launcher data directory inside the Skyrim directory (local database, backups). |
| `LauncherLocalDatabaseFileName` | Per-installation SQLite database in that directory. The global database in the launchers root is always `launcher.db`. |
| `LauncherOAuthTokenEntropy` | DPAPI entropy protecting stored OAuth tokens. |
| `LauncherEnvironmentVariablePrefix` | Prefix of configuration environment variables (`MYLAUNCHER_Urls__Feed__MainUrl`). |
| `LauncherAppExecutableName` | Launcher executable file name, used by the installer and logs collection. |
| `LauncherDisplayName` | Product name shown to users (window, shortcuts, Programs and Features). |
| `LauncherUserAgentProduct` | HTTP User-Agent product token. |
| `LauncherModOrganizerDirectoryName` | Mod Organizer directory inside the Skyrim directory. |
| `LauncherModpackProfileName` | Mod Organizer profile of the modpack. |
| `LauncherModpackVersionFile` | File, relative to the Skyrim directory, holding the installed modpack version: only the version string, with no line break. |
| `LauncherUserModsSeparator` | Mod Organizer separator under which users' own mods are kept. |
| `LauncherRequiredSkyrimVersion` | `SkyrimSE.exe` version the launcher accepts. |
| `LauncherRegistryKey` | `HKEY_CURRENT_USER\...` key shared by the installer and the launcher. |
| `LauncherManufacturer` | Installer manufacturer. |
| `LauncherInstallerPackageId`, `LauncherInstallerBundleId` | WiX derives the MSI and bundle upgrade codes from these. |
| `LauncherInstallerOutputName` | Setup file name: `out/{name}_{version}.exe`/`.msi`. Keep it a literal. |
| `LauncherIconFile`, `LauncherInstallerLogoFile` | Shortcut/Programs and Features icon and the 64x64 setup window logo. |
| `LauncherInstallerProject` | The consumer's MSI layout project (step 6). |

Every value except `VersionPrefix` is persisted identity. Changing one strands existing installations, databases, linked accounts or upgrades, so a change needs a migration.

Each C# project that imports the product props gets a generated `{LauncherProductNamespace}.ProductIdentity` class: a constant per value and `CreateOptions()`, which returns the `LauncherProductOptions` the kit requires.

### 3. Launcher executable

The project imports the product props and references `LauncherKit.Desktop`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<Import Project="..\..\MyLauncher.Product.props"/>
	<PropertyGroup>
		<OutputType>WinExe</OutputType>
		<PublishAot>true</PublishAot>
		<!-- Required: SDK 11's optimized AOT publish drops Avalonia resources and compiled XAML. -->
		<UseOptimizedPublish>false</UseOptimizedPublish>
		<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
		<ApplicationManifest>app.manifest</ApplicationManifest>
		<ApplicationIcon>Assets\favicon.ico</ApplicationIcon>
	</PropertyGroup>
	<ItemGroup>
		<AvaloniaResource Include="Assets\**"/>
		<None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest"/>
		<!-- Serilog cannot capture objects as structures after trimming. -->
		<RuntimeHostConfigurationOption Include="Serilog.Capturing.IsStructureValueSupported" Value="false" Trim="true"/>
	</ItemGroup>
	<ItemGroup>
		<PackageReference Include="Avalonia"/>
		<PackageReference Include="Avalonia.Desktop"/>
		<PackageReference Include="CommunityToolkit.Mvvm"/>
		<PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
		<PackageReference Include="R3Extensions.Avalonia"/>
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="..\..\kit\src\LauncherKit.Desktop\LauncherKit.Desktop.csproj"/>
	</ItemGroup>
	<!-- Development automation, compiled in only with -p:EnableDevAutomation=true (never by PublishLauncher). -->
	<PropertyGroup Condition="'$(EnableDevAutomation)' == 'true'">
		<DefineConstants>$(DefineConstants);DEV_AUTOMATION</DefineConstants>
	</PropertyGroup>
	<ItemGroup Condition="'$(EnableDevAutomation)' == 'true'">
		<ProjectReference Include="..\..\kit\src\LauncherKit.DevAutomation\LauncherKit.DevAutomation.csproj"/>
	</ItemGroup>
</Project>
```

The NativeAOT publish fails on any trim/AOT warning. When a third-party package's remaining warnings have been checked by hand, collapse only that assembly's warnings with a `CollapseCheckedThirdPartyTrimWarnings` target (`TrimmerSingleWarn` on its `ResolvedFileToPublish` items, `TrimmerSingleWarn=false` and `NoWarn` `IL2104;IL3053` on the project). Re-check the warnings when updating that package.

`Program.cs`:

```csharp
using Avalonia;
using LauncherKit.Desktop;

namespace MyLauncher.App;

sealed class Program
{
	[STAThread]
	public static void Main(string[] args) => LauncherKitProgram.Run<App>(args);

	// Used by the Avalonia designer.
	public static AppBuilder BuildAvaloniaApp() => LauncherKitProgram.BuildAvaloniaApp<App>();
}
```

`App` derives from `LauncherKitApp`, which owns logging, configuration, kit service registrations, the application scope, localization, the view locator, scope reloads and disposal:

```csharp
public sealed class App : LauncherKitApp
{
	protected override LauncherProductOptions CreateProductOptions() => ProductIdentity.CreateOptions();

	// Must be written in the class named by App.axaml's x:Class.
	protected override void LoadXaml() => AvaloniaXamlLoader.Load(this);

	// Values compiled in rather than shipped in appsettings.json; files and environment variables override them.
	protected override IEnumerable<KeyValuePair<string, string?>> GetInMemoryConfiguration() =>
	[
		new("App:Language", "en"),
		new("Urls:Feed:BearerToken", "<feed server token>"),
		new("Urls:Analytics:BearerToken", "<analytics token>"),
	];

	protected override void ConfigureServices(LauncherKit.Core.AppBuilder builder)
	{
		var services = builder.Services;
		services.AddSingleton<ILaunchersRootDirectoryManager>(sp => new MyLaunchersRootDirectoryManager(true,
			sp.GetRequiredService<IFileSystemUtils>(), sp.GetRequiredService<IRegistryManager>(),
			sp.GetRequiredService<IOptions<LauncherProductOptions>>().Value));
		services.AddSingleton<IViewRegistry, MyViewRegistry>();
		services.AddScoped<MainWindowViewModelBase, MainWindowViewModel>();
		services.AddSingleton<MainWindow>();
		services.AddSingleton<IMainView>(sp => sp.GetRequiredService<MainWindow>());
		services.AddSingleton<INotificationManagerProvider>(sp => sp.GetRequiredService<MainWindow>());
		services.AddSingleton<IDialogService, MyDialogService>();
		// Views, dialogs and other consumer services.
	}

	// Rebuilds views after a scope reload (e.g. a language change).
	protected override void ReloadViews(Window mainWindow) => ((MainWindow)mainWindow).ReloadViews();
}
```

Optional hooks:
- `BeforeConfiguration`: runs before the configuration is read, e.g. to migrate files.
- `OnServicesBuilt`: runs after the services are built, before the XAML is loaded.
- `StartDesktopServices`: starts services that need the desktop lifetime, e.g. `DevAutomationServer.Start` under `DEV_AUTOMATION`.

The consumer must register:

| Service | Contract |
|---|---|
| `ILaunchersRootDirectoryManager` | A `LaunchersRootDirectoryManager` subclass describing the installation layout (step 5). |
| `IViewRegistry` | Maps each kit or consumer view model to its view; throws for unknown models. `ViewLocator` uses it. |
| `MainWindowViewModelBase` (scoped) | The consumer's main window model: pages, navigation, setup-versus-home selection (show `FirstSetupViewModel` only when `IsRequired`, i.e. it has steps). Kit page view models (`HomeViewModel`, `FirstSetupViewModel`, `SettingsViewModel`, `DownloadsViewModel`, `GraphPacksViewModel`, `OptionalModsViewModel`, `AppTaskQueueViewModel`, ...) are resolved from the scope. |
| `IMainView` (singleton) | The main window; it must derive from Avalonia `Window`, with any base class. |
| `IDialogService` (`LauncherKit.Desktop.Services`, singleton) | `ShowYesNoConfirmation(header, message, defaultToNo, cancellationToken)`: a yes/no question, closed unanswered (returning `false`) when the token is cancelled. The kit's main-window close confirmation uses it; the consumer may extend the interface with its own dialogs and register the implementation under both. |

View models expose typed facts and act directly. Views own the wording, confirmations, dialogs, notifications and directory pickers, and pass collected input as command parameters. Use compiled bindings, and use `ObjectConverters.IsNull`/`IsNotNull` (never `!obj`) to bind visibility to whether an object is null. AGENTS.md "Desktop contract" lists all the rules.

#### Closing the main window

The kit attaches its `Closing` handler to the main window. Application/OS shutdown and owner closing close at once. When the user closes the window, the close is cancelled and the current scope's `IAppCloseHandler`s (`LauncherKit.Desktop.Services`, `Handle(Window mainWindow)`) run in registration order; the window closes when none returns `CannotClose`. A failing handler is logged and skipped, and close requests while handlers run are ignored. The kit registers two handlers: the scoped pending-tasks confirmation (with unfinished tasks it asks through `IDialogService`, `DesktopStrings` `PendingTasksCloseTitle`/`PendingTasksCloseMessage`, "No" by default, and the question closes itself and allows the close once the queue empties) and the singleton window-state save into `AllSettings.Window` (nothing when minimized; a maximized window keeps its saved normal bounds). Consumers add their own with `AddScoped<IAppCloseHandler, ...>()` (or singleton). The kit also restores the saved size, position and maximized state before showing the main window, so consumer windows handle neither.

#### Notifications

The kit registers `INotificationService` (`LauncherKit.Desktop.Services`, singleton, UI thread only). `Show(AppNotification)` keeps a toast until the user clicks it or `Close` removes it. It shows toasts only when the consumer registers an `INotificationManagerProvider`, usually its main window: its `WindowNotificationManager` is resolved on the first notification, so the service never creates the window, and must be created with the window, because a manager whose template is not applied yet drops the first toast. Without a provider, `Show` and `Close` do nothing. The kit ships no toast template: add an application-wide `DataTemplate` for `AppNotification` (`Title`, optional `Message`, `IsInProgress`), because the manager lives in the window's adorner layer. View models never use the service; views and initializers do. The kit's `FeedLoadNotificationsInitializer` uses it to show feed loading progress and load errors in every launcher (texts `FeedLoading`, `FeedLoadFailed` and `FeedUnavailableFeatures` in `DesktopStrings`, overridable like other kit texts).

#### First setup steps

`FirstSetupViewModel` routes through the steps of every registered `ISetupStepProvider`, in ascending `Order`. The kit registers providers for the language, the Skyrim directory, the data directory and the download server check. The language step (`en`/`ru`, no operating-system option) appears until `App:Language` holds one of them, so set `App:Language` in `GetInMemoryConfiguration()` to fix the language and skip it. To add a step, derive from `SetupStepViewModel`: validate and apply your input to `FirstSetupContext.Settings`, then call `GoNext()`. Map the step to a view in your `IViewRegistry`, and register a provider:

```csharp
public sealed class TermsSetupStepProvider(ITermsStore terms) : ISetupStepProvider
{
	public int Order => SetupStepOrder.SkyrimDirectory - 50; // before the Skyrim directory

	public IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context) =>
		terms.IsAccepted ? [] : [new TermsSetupStepViewModel(terms)];
}

services.TryAddEnumerable(ServiceDescriptor.Scoped<ISetupStepProvider, TermsSetupStepProvider>());
```

A provider returns no steps when its settings are complete. The setup is shown only while some provider returns a step. Moving past the last step saves the settings and sets `State` to `Finished`. The application scope belongs to the Skyrim directory and is replaced only when `App:SkyrimDirectory` changes, so a setup that sets it gets a new scope with the consumer's normal pages; after a setup that does not, the consumer replaces the setup page itself on `Finished`.

#### Localization

`DesktopStrings` provides English and Russian default texts for kit facts (tasks, errors, choices). To override or add texts, put `Localization/LauncherKit/DesktopStrings.resx` (plus culture files such as `DesktopStrings.ru.resx`) in the launcher project. The resource name must be `{assembly name}.Localization.LauncherKit.DesktopStrings`. Each culture looks up the consumer's resources first, then the kit's. `App:Language` selects the culture.

### 4. Configuration

The configuration is read in this order, each source overriding the previous: `GetInMemoryConfiguration()`, `appsettings.json` and `appsettings.override.json` from the launcher's working directory (the user's settings, written by the launcher), then environment variables with `LauncherEnvironmentVariablePrefix`.

```json
{
	"Feed": { "FeedPath": "feedV11.json" },
	"Urls": {
		"Feed": { "MainUrl": "https://feed.example.com", "ProxyUrl": "https://proxy.feed.example.com" },
		"Analytics": { "MainUrl": "https://analytics.example.com", "ProxyUrl": "https://proxy.analytics.example.com" }
	},
	"Discord": { "ClientId": "<application id>", "GuildId": "<server id>", "RedirectPort": 53134 }
}
```

- `Urls:<key>` needs `MainUrl`; `ProxyUrl` (a fallback server) and `BearerToken` are optional. With a proxy, a request that fails because of the server is retried once on the other server, which then stays preferred for the run. Without one, requests go to `MainUrl` only.
- The feed is `{Urls:Feed}/{Feed:FeedPath}`; the downloads, logos and changelogs it references are paths on the same server (the bucket root). Edit it with the feed editor.
- `Discord` is optional: without `Discord:ClientId`, `SettingsViewModel.DiscordIntegration` is null and the consumer hides Discord linking. The Discord application must be a public client with `http://127.0.0.1:{RedirectPort}/discord/callback` registered as a redirect URI.
- `Download`, `App` and `Window` hold user settings that the launcher writes to `appsettings.override.json`.

### 5. Installation layout

`LaunchersRootDirectoryManager` is abstract: the consumer decides where launchers are installed.

```csharp
public sealed class MyLaunchersRootDirectoryManager(
	bool useCurrentDirectory, IFileSystemUtils fileSystemUtils, IRegistryManager registryManager,
	LauncherProductOptions product)
	: LaunchersRootDirectoryManager(useCurrentDirectory, fileSystemUtils, registryManager, product)
{
	// The launcher runs from a subdirectory of the launchers root.
	protected override string RootFromCurrentDirectory(string currentDirectory) =>
		Path.GetDirectoryName(currentDirectory) ?? currentDirectory;

	public override string? TryGetInstalledLauncherDirectory() => Path.Combine(Directory, "app");
}
```

The launcher passes `useCurrentDirectory: true`: the root comes from its working directory, so development, sandbox and copied launchers never touch an installed root. The logs collector passes `false`: the root is the `LaunchersRoot` registry value that the installer writes. The launchers root holds `launcher.db`, `Tools\` and other launcher-wide state; it must match the installer layout in step 6.

### 6. Installer

The kit builds a per-user MSI (no UAC) and a Burn setup bundle `{LauncherInstallerOutputName}_{version}.exe` from the product props. The consumer supplies only the layout project named by `LauncherInstallerProject`. It is not part of the consumer solution; `PublishInstaller` builds it.

```xml
<Project Sdk="WixToolset.Sdk/7.0.1">
	<Import Project="..\..\MyLauncher.Product.props"/>
	<Import Project="..\..\kit\installer\LauncherKit.Installer.props"/>
</Project>
```

`Layout.wxs` in the same project defines the `LAUNCHERSROOT` (launchers root) and `LAUNCHERDIR` (launcher files) directories. It may add components to the `Launcher` feature with `FeatureRef`.

```xml
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
	<Fragment>
		<StandardDirectory Id="LocalAppDataFolder">
			<Directory Id="LAUNCHERSROOT" Name="MyLauncher">
				<Directory Id="LAUNCHERDIR" Name="app" />
			</Directory>
		</StandardDirectory>
	</Fragment>
</Wix>
```

The package and bundle provide:
- Start menu and desktop shortcuts, chosen on the setup's Install page and kept across upgrades.
- Closing a running launcher before installing, and starting the launcher after install.
- The registry values the launcher and logs collector read.
- A "remove all data" option when uninstalling, which deletes the launchers root and the registered download and temp folders.

To let the user choose the install folder, set `LauncherInstallerDefaultFolder` in the product props to the layout's default root as a formatted path, e.g. `<LauncherInstallerDefaultFolder>[LocalAppDataFolder]Programs\MyLauncher</LauncherInstallerDefaultFolder>`. The setup then has an Options button with a folder page starting from it (`<setup>.exe /quiet InstallFolder=D:\Games\MyLauncher` on the command line; `msiexec /i <package>.msi LAUNCHERSROOT=...` for the MSI alone). Once a launcher is installed its folder is kept: the Options button is hidden and upgrades stay in the installed root. Because the chosen folder may hold other files, "remove all data" then deletes only the launcher directory, `Tools\` and `launcher.db`, and the root only if it is left empty. Without the property the root is the layout's fixed folder and the setup shows no folder choice.

The setup window is in Russian (`installer/Bundle/Theme.ru-ru.wxl`).

The bundle version is `VersionPrefix[-VersionSuffix]` and is shown in Programs and Features. Burn refuses to install an older setup over a newer one. The launcher offers only newer versions from the feed, so this only affects a manually run older setup. A launcher update downloads the setup from the feed, starts it with `/passive` and exits; the setup restarts the launcher.

### 7. Logs collector

A console executable importing the product props and referencing `LauncherKit.LogsCollector.Core`. It shares the root directory manager source with the launcher (for example through a linked `Compile` item).

```csharp
await LogsCollectorRunner.Run(
	static (fileSystemUtils, registryManager) =>
		new MyLaunchersRootDirectoryManager(false, fileSystemUtils, registryManager, ProductIdentity.CreateOptions()),
	static error => $"Log collection failed: {error.Message}",
	"Done, press any key...");
```

It writes the launcher, Mod Organizer and SKSE logs into `collected_logs.zip` in the current directory.

### 8. Build

The NUKE host (`build/_build.csproj`, referencing `kit/src/LauncherKit.Build/LauncherKit.Build.csproj`) derives from `LauncherBuild`:

```csharp
class Build : LauncherBuild
{
	public static int Main() => Execute<Build>(x => x.PublishInstaller, x => x.PublishConsoleCollector);

	protected override string LauncherProjectName => "MyLauncher.App";
	protected override string CollectorProjectName => "MyLauncher.LogsCollector";
	protected override string CollectorExecutableName => "LogsCollector";
	protected override string FeedEditorProjectName => "LauncherKit.FeedEditor";
	protected override AbsolutePath ProductPropsFile => RootDirectory / "MyLauncher.Product.props";
}
```

| Target | Result |
|---|---|
| `PublishLauncher` | NativeAOT launcher in `out/publish/Launcher`, with development automation disabled. |
| `CleanLauncherSymbols` | Removes `.pdb` files from the launcher output. |
| `PublishInstaller` | `out/{name}_{version}.exe` (setup) and `.msi`. |
| `PublishConsoleCollector` | NativeAOT logs collector. |
| `PublishConsoleHelper` | `LauncherKit.ConsoleHelper`. |
| `PublishFeedEditor` | Self-contained feed editor in `out/FeedEditor-{runtime}`. |
| `Test` | All tests, with TRX and coverage in `out/test-results`. |
| `Clean` | Deletes `out/`. |

```powershell
.\build.ps1                              # default targets
.\build.ps1 PublishLauncher
.\build.ps1 --version-suffix beta.42     # prerelease: 1.0.0-beta.42
```

### 9. Feed, feed editor and analytics

- **Feed.** `feedV11.json` lists launcher versions, modpacks and their patches and changelogs, install components, prerequisites, graph packs and optional mods. Text is localized with a required `DefaultLanguage`. Downloads carry SHA-256 hashes, and install steps are declarative (`installMod`, `extractArchive`, `removeFileSystemItem`, `mergePlugins`, ...). The C# contracts are in `src/LauncherKit.Core/Feed`.
- **Feed editor.** Edits the feed stored in a Cloudflare R2 bucket; each user signs in with their own R2 credentials. The consumer supplies its bucket as `R2:ServiceUrl` and `R2:BucketName` (`R2:FeedKey` defaults to `feedV11.json`), e.g. as `R2__ServiceUrl`/`R2__BucketName` environment variables. Run it with `dotnet run --project kit/src/LauncherKit.FeedEditor`, or in Docker: its `Dockerfile` builds from the consumer root, with a `compose.yaml` and `.dockerignore` there. The bucket needs CORS rules for direct uploads (see AGENTS.md).
- **Analytics.** `workers/analytics` receives the installation reports the launcher sends on every start. The consumer owns the Wrangler configuration (routes, dataset, secrets):

```powershell
cd kit/workers/analytics
npm ci
npm run deploy -- --config ../../../workers/analytics/wrangler.jsonc
```

## Developing the kit

```powershell
dotnet build LauncherKit.sln -c Debug   # static analysis: warnings are errors
dotnet test LauncherKit.sln -c Debug
```

The kit builds and tests without a consumer. Changes that need a running launcher (UI, installs, AOT publish, installer) are exercised in a consumer whose `kit/` submodule points at the change, with the [dev automation](docs/dev-automation.md) sandbox. Contributor rules, contracts and test conventions are in [AGENTS.md](AGENTS.md).
