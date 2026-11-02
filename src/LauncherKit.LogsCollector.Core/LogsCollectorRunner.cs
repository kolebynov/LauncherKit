using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

public static class LogsCollectorRunner
{
	public static async Task Run(LauncherProductOptions product, Func<Exception, string> errorMessage,
		string completionPrompt, CancellationToken cancellationToken = default)
	{
		product.Validate();

		using var loggerFactory = new SimpleConsoleLoggerFactory(TimeProvider.System);
		var fileSystemUtils = new FileSystemUtils(loggerFactory.CreateLogger<FileSystemUtils>());
		var rootDirectoryManager = new LaunchersRootDirectoryManager(false, fileSystemUtils, product);
		var collector = new LogsCollector(fileSystemUtils,
			[new LauncherLogsSource(rootDirectoryManager), new SkseLogsSource(fileSystemUtils), new ModOrganizerLogsSource()],
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
