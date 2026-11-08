using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

public static class LogsCollectorRunner
{
	/// <param name="createRootDirectoryManager">
	/// Creates the consumer's root directory manager reading the root from the registry (not from the current directory).
	/// </param>
	public static async Task Run(
		Func<IFileSystemUtils, IRegistryManager, ILaunchersRootDirectoryManager> createRootDirectoryManager,
		Func<Exception, string> errorMessage, string completionPrompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(createRootDirectoryManager);

		using var loggerFactory = new SimpleConsoleLoggerFactory(TimeProvider.System);
		var fileSystemUtils = new FileSystemUtils(loggerFactory.CreateLogger<FileSystemUtils>());
		var rootDirectoryManager = createRootDirectoryManager(fileSystemUtils, new RegistryManager());
		var collector = new LogsCollector(fileSystemUtils,
			[new LauncherLogsSource(rootDirectoryManager), new SkseLogsSource(fileSystemUtils), ModOrganizerLogsSource.Instance],
			loggerFactory.CreateLogger<LogsCollector>(), TimeProvider.System);
		var error = await collector.CollectLogs(Directory.GetCurrentDirectory(), null, cancellationToken);

		Console.WriteLine();
		Console.WriteLine();

		if (error != null)
		{
			Console.WriteLine(errorMessage(error));
		}

		Console.Write(completionPrompt);
		Console.ReadKey();
	}
}
