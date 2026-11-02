using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

public sealed class ProcessRunner : IProcessRunner
{
	private readonly ILogger<ProcessRunner> _logger;

	public ProcessRunner(ILogger<ProcessRunner> logger)
	{
		_logger = logger;
	}

	public async Task StartProcessAndAssertSuccess(ProcessStartInfo startInfo, bool assert, CancellationToken cancellationToken,
		Action<string?>? outputProcessor = null)
	{
		var exitCode = await StartProcess(startInfo, cancellationToken, outputProcessor);
		if (assert && exitCode != 0)
		{
			throw new InvalidOperationException($"Process {startInfo.FileName} exited with code {exitCode}");
		}
	}

	public async Task<int> StartProcess(ProcessStartInfo startInfo, CancellationToken cancellationToken,
		Action<string?>? outputProcessor = null)
	{
		_logger.LogInformation("Starting process: {ProcessName} {Arguments}", startInfo.FileName, startInfo.Arguments);

		using var process = new Process();
		process.StartInfo = startInfo;

		if (!startInfo.UseShellExecute)
		{
			startInfo.RedirectStandardError = startInfo.RedirectStandardOutput = true;
			startInfo.StandardOutputEncoding = startInfo.StandardErrorEncoding = Encoding.UTF8;

			var processName = Path.GetFileName(startInfo.FileName);
			process.OutputDataReceived += (_, args) =>
			{
				if (outputProcessor != null)
				{
					outputProcessor.Invoke(args.Data);
				}
				else
				{
					_logger.LogInformation("[Process: {ProcessName}] {Output}", processName, args.Data);
				}
			};
			process.ErrorDataReceived += (_, args) =>
				_logger.LogError("[Process: {ProcessName}] {Output}", processName, args.Data);
		}

		if (!process.Start())
		{
			throw new InvalidOperationException("Failed to start process");
		}

		if (!startInfo.UseShellExecute)
		{
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}

		using var registration = cancellationToken.Register(() =>
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch (InvalidOperationException)
			{
				// Process exited between cancellation and the kill attempt.
			}
			catch (Win32Exception)
			{
				// The operating system rejected a kill after the process had already transitioned.
			}
		});
		await process.WaitForExitAsync(cancellationToken);

		_logger.LogInformation("Process {ProcessName} exited with code {ExitCode}", startInfo.FileName, process.ExitCode);

		return process.ExitCode;
	}
}