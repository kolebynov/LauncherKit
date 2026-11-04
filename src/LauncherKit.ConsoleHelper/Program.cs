using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Core;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

using var cancelCts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) => cancelCts.Cancel();

Console.WriteLine("Select option:");
Console.WriteLine("1. Generate beta code hash");
Console.WriteLine("2. Calculate SHA256 file hash");
Console.WriteLine("3. Test download");

try
{
	var option = ReadOption();
	switch (option)
	{
		case 1:
			HandleBetaCodeHash();
			break;
		case 2:
			await HandleFileHash();
			break;
		case 3:
			await HandleTestDownload(cancelCts.Token);
			break;
	}
}
catch (OperationCanceledException)
{
}

static int ReadOption()
{
	while (true)
	{
		if (int.TryParse(Console.ReadLine(), CultureInfo.InvariantCulture, out var option))
		{
			return option;
		}

		Console.WriteLine("Invalid option. Please enter a valid number.");
	}
}

static void HandleBetaCodeHash()
{
	Console.Write("Enter beta code: ");
	var betaCode = Console.ReadLine()!;
	Console.WriteLine($"Hash: {FeedProvider.HashBetaCode(betaCode)}");
}

static async Task HandleFileHash()
{
	Console.Write("Enter file path: ");
	var filePath = Console.ReadLine()!;
	var hash = await new HashCalculator().HashFile(filePath, null, CancellationToken.None);
	Console.WriteLine($"Hash: {hash}");
}

static async Task HandleTestDownload(CancellationToken cancellationToken)
{
	var appBuilder = new AppBuilder(new LauncherProductOptions
	{
		SkyrimDataDirectoryName = ".LauncherDownloadHelper",
		LocalDatabaseFileName = "helper-installation.db",
		OAuthTokenEntropy = "LauncherDownloadHelper.OAuthToken",
		EnvironmentVariablePrefix = "LAUNCHER_DOWNLOAD_HELPER_",
		AppExecutableName = "ConsoleHelper.exe",
		DisplayName = "Launcher Download Helper",
		UserAgentProduct = "LauncherDownloadHelper",
		ModOrganizerDirectoryName = "Mod Organizer",
		ModpackProfileName = "DownloadHelper",
		ModpackVersionFile = Path.Combine("Mod Organizer", "version.txt"),
		UserModsSeparator = "USER MODS",
		RequiredSkyrimVersion = "1.5.97.0",
		RegistryKey = @"HKEY_CURRENT_USER\Software\LauncherKit\DownloadHelper",
	});
	// The feed to download from comes from the environment, e.g. LAUNCHER_DOWNLOAD_HELPER_Feed__FeedPath=feedV11.json,
	// LAUNCHER_DOWNLOAD_HELPER_Urls__Feed__MainUrl and the optional LAUNCHER_DOWNLOAD_HELPER_Urls__Feed__ProxyUrl.
	var configuration = appBuilder.Configuration;
	configuration.AddInMemoryCollection(new Dictionary<string, string?>
	{
		{ "App:AppDataDirectory", Environment.CurrentDirectory },
	});
	configuration.AddEnvironmentVariables(appBuilder.Product.EnvironmentVariablePrefix);
	string[] requiredKeys = ["Feed:FeedPath", "Urls:Feed:MainUrl"];
	var missingKeys = requiredKeys.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToArray();
	if (missingKeys.Length > 0)
	{
		Console.WriteLine("Set the feed to download from: " + string.Join(", ", missingKeys.Select(key =>
			appBuilder.Product.EnvironmentVariablePrefix + key.Replace(":", "__", StringComparison.Ordinal))));
		return;
	}

	appBuilder.Services.AddSingleton<ILaunchersRootDirectoryManager>(services => new HelperRootDirectoryManager(
		services.GetRequiredService<IFileSystemUtils>(), services.GetRequiredService<IRegistryManager>(),
		appBuilder.Product));
	using var app = appBuilder.Build();
	var sp = app.ServiceProvider;
	var feedSourceFactory = sp.GetRequiredService<IFeedFileSourceFactory>();
	var downloader = sp.GetRequiredService<IFileSourceDownloader>();
	var fileStorage = sp.GetRequiredService<IFileStorageService>();
	var fileSystemUtils = sp.GetRequiredService<IFileSystemUtils>();

	fileSystemUtils.SafeDeleteFile(Path.Combine("Downloads", "Skyrim.7z"));
	fileSystemUtils.SafeDeleteFile(Path.Combine("Downloads", "Skyrim.7z.part"));
	var fileSource = feedSourceFactory.Create(new DownloadInfo { S3Path = "Skyrim.7z" },
		sp.GetRequiredService<IUrlResolver>().GetUri(UrlKeys.Feed, DownloadServer.Main));
	{
		await using var openedFile = fileStorage.GetOrCreate("Skyrim.7z", await fileSource.GetFileSize(cancellationToken), null) switch
		{
			IFileStorageService.GetOrCreateResult.Existing(FileDescriptor.Opened(var opened)) => opened,
			IFileStorageService.GetOrCreateResult.Existing or IFileStorageService.GetOrCreateResult.NeedRehash => null,
			IFileStorageService.GetOrCreateResult.NeedAllocate => fileStorage.Allocate("Skyrim.7z", 6),
		};
		if (openedFile == null)
		{
			Console.WriteLine("File already downloaded");
			return;
		}

		await downloader.Download(fileSource, openedFile, p =>
		{
			Console.WriteLine($"Speed: {p.Speed / 1024 / 1024}MB/s");
			return ValueTask.CompletedTask;
		}, cancellationToken);
	}

	fileStorage.Complete("Skyrim.7z");
	Console.WriteLine("Download finished");
}

// The helper is never installed: like a launcher run from its build output, its root is its working directory's parent.
internal sealed class HelperRootDirectoryManager(
	IFileSystemUtils fileSystemUtils, IRegistryManager registryManager, LauncherProductOptions product)
	: LaunchersRootDirectoryManager(true, fileSystemUtils, registryManager, product)
{
	public override string? TryGetInstalledLauncherDirectory() => null;

	protected override string RootFromCurrentDirectory(string currentDirectory) =>
		Path.GetDirectoryName(currentDirectory) ?? currentDirectory;
}