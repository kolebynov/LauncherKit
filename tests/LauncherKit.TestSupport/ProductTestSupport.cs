using LauncherKit.Infrastructure;

namespace LauncherKit.TestSupport;

public static class ProductTestSupport
{
	public static LauncherProductOptions CreateProduct() => new()
	{
		RootDirectoryName = "ExampleLauncher",
		SkyrimDataDirectoryName = ".ExampleLauncher",
		GlobalDatabaseFileName = "example-global.db",
		LocalDatabaseFileName = "example-local.db",
		OAuthTokenEntropy = "ExampleLauncher.OAuthToken",
		EnvironmentVariablePrefix = "EXAMPLE_LAUNCHER_",
		AppExecutableName = "ExampleLauncher.exe",
		DisplayName = "Example Modpack",
		UserAgentProduct = "ExampleLauncher",
		ModOrganizerDirectoryName = "Example MO",
		ModpackProfileName = "Example",
		ModpackVersionFile = Path.Combine("Example MO", "mods", "Example", "version.txt"),
		UserModsSeparator = "USER MODS",
		RequiredSkyrimVersion = "1.5.97.0",
		RegistryKey = @"HKEY_CURRENT_USER\Software\Example\Example Launcher",
		FirstSetupHelpUri = new Uri("https://example.test/setup"),
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
