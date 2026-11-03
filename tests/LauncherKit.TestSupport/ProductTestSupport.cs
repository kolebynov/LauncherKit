using LauncherKit.Infrastructure;

namespace LauncherKit.TestSupport;

public static class ProductTestSupport
{
	public static LauncherProductOptions CreateProduct(
		string skyrimDataDirectoryName = ".ExampleLauncher",
		string globalDatabaseFileName = "example-global.db",
		string localDatabaseFileName = "example-local.db",
		string oauthTokenEntropy = "ExampleLauncher.OAuthToken",
		string environmentVariablePrefix = "EXAMPLE_LAUNCHER_",
		string modOrganizerDirectoryName = "Example MO") => new()
	{
		SkyrimDataDirectoryName = skyrimDataDirectoryName,
		GlobalDatabaseFileName = globalDatabaseFileName,
		LocalDatabaseFileName = localDatabaseFileName,
		OAuthTokenEntropy = oauthTokenEntropy,
		EnvironmentVariablePrefix = environmentVariablePrefix,
		AppExecutableName = "ExampleLauncher.exe",
		DisplayName = "Example Modpack",
		UserAgentProduct = "ExampleLauncher",
		ModOrganizerDirectoryName = modOrganizerDirectoryName,
		ModpackProfileName = "Example",
		ModpackVersionFile = Path.Combine("Example MO", "mods", "Example", "version.txt"),
		UserModsSeparator = "USER MODS",
		RequiredSkyrimVersion = "1.5.97.0",
		RegistryKey = @"HKEY_CURRENT_USER\Software\Example\Example Launcher",
		GameLaunchVariants =
		[
			new() { Id = "Game", ExecutablePath = "example-loader.exe" },
			new() { Id = "ModOrganizer" },
			new() { Id = "BethIni", ExecutablePath = Path.Combine("Tools", "ExampleIni.exe") },
		],
	};

	public static AutoMocker CreateMocker(MockBehavior behavior = MockBehavior.Strict)
	{
		var mocker = new AutoMocker(behavior);
		var product = CreateProduct();
		OptionsStubs.Options(mocker, product);
		mocker.Use(product);
		return mocker;
	}
}
