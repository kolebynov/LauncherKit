using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

public sealed class SevenZipService : ISevenZipService
{
	private readonly IProcessRunner _processRunner;
	private readonly IDiskSpaceChecker _diskSpaceChecker;
	private readonly string _sevenZipPath;
	private readonly string _sevenZipGPath;

	public SevenZipService(IProcessRunner processRunner, ILaunchersRootDirectoryManager launchersRootDirectoryManager,
		IDiskSpaceChecker diskSpaceChecker)
	{
		_processRunner = processRunner;
		_diskSpaceChecker = diskSpaceChecker;
		_sevenZipPath = launchersRootDirectoryManager.SevenZipExePath;
		_sevenZipGPath = launchersRootDirectoryManager.SevenZipGExePath;
	}

	public async Task ExtractArchive(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(archivePath);
		ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);

		await CheckAvailableDiskSpace(archivePath, destinationDirectory, null, cancellationToken);
		await ExecuteExtractSevenZip($"x \"{archivePath}\" -o\"{destinationDirectory}\"", cancellationToken);
	}

	public async Task ExtractArchiveExcept(string archivePath, string destinationDirectory, string excludedEntryPath,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(archivePath);
		ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
		ArgumentException.ThrowIfNullOrEmpty(excludedEntryPath);

		// The excluded entry is at most one file, so checking space for the whole archive is close enough.
		await CheckAvailableDiskSpace(archivePath, destinationDirectory, null, cancellationToken);
		await ExecuteExtractSevenZip($"x \"{archivePath}\" -o\"{destinationDirectory}\" \"-x!{excludedEntryPath}\"",
			cancellationToken);
	}

	public async Task ExtractEntriesFromArchive(IEnumerable<string> entryPathsInArchive, string archivePath,
		string destinationDirectory, bool preserveFullPath, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(archivePath);
		ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);

		var pathsInArchive = string.Join(' ', entryPathsInArchive.Select(x => $"\"{x}\""));
		await CheckAvailableDiskSpace(archivePath, destinationDirectory, pathsInArchive, cancellationToken);
		var command = preserveFullPath ? 'x' : 'e';

		await ExecuteExtractSevenZip($"{command} \"{archivePath}\" -o\"{destinationDirectory}\" {pathsInArchive}", cancellationToken);
	}

	public async Task<bool> ContainsEntry(string archivePath, string entryPathInArchive, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(archivePath);
		ArgumentException.ThrowIfNullOrEmpty(entryPathInArchive);

		return await ListArchiveContent(archivePath, $"\"{entryPathInArchive}\"", cancellationToken)
			.AnyAsync(cancellationToken);
	}

	public IAsyncEnumerable<ArchiveEntry> ListArchiveEntries(string archivePath, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(archivePath);

		return ListArchiveContent(archivePath, null, cancellationToken);
	}

	private Task ExecuteExtractSevenZip(string arguments, CancellationToken cancellationToken) =>
		ExecuteSevenZip($"{arguments} -y -bb0 -bt", cancellationToken, useGui: true);

	private async Task CheckAvailableDiskSpace(string archivePath, string destinationDirectory, string? pathsInArchive,
		CancellationToken cancellationToken)
	{
		var extractedSize = await ListArchiveContent(archivePath, pathsInArchive, cancellationToken)
			.Select(x => x.Size)
			.SumAsync(cancellationToken);
		_diskSpaceChecker.CheckAvailableDiskSpace(destinationDirectory, extractedSize);
	}

	private IAsyncEnumerable<ArchiveEntry> ListArchiveContent(
		string archivePath, string? pathsInArchive, CancellationToken cancellationToken)
	{
		var channel = Channel.CreateUnbounded<ArchiveEntry>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = true,
		});

		var isListingContents = false;
		ArchiveListingLayout? layout = null;

		_ = ExecuteSevenZip(
			$"l \"{archivePath}\" {pathsInArchive}", cancellationToken,
			logOutput: line =>
			{
				if (line == null)
				{
					return;
				}

				if (line.StartsWith("----", StringComparison.Ordinal))
				{
					isListingContents = !isListingContents;
					if (layout == null)
					{
						Span<Range> ranges = stackalloc Range[5];
						line.AsSpan().Split(ranges, ' ', StringSplitOptions.RemoveEmptyEntries);
						layout = new ArchiveListingLayout
						{
							AttributesRange = ranges[1],
							SizeRange = ranges[2],
							PathColumnIndex = ranges[4].Start.Value,
						};
					}

					return;
				}

				if (isListingContents)
				{
					var lineSpan = line.AsSpan();
					var size = long.Parse(lineSpan[layout!.SizeRange], CultureInfo.InvariantCulture);
					var isDirectory = lineSpan[layout.AttributesRange].StartsWith('D');
					var path = lineSpan[layout.PathColumnIndex..].Trim().ToString();
					channel.Writer.TryWrite(new ArchiveEntry(path, size, isDirectory));
				}
			})
			.ContinueWith(t => channel.Writer.TryComplete(t.Exception?.GetBaseException()), cancellationToken,
				TaskContinuationOptions.None, TaskScheduler.Current);

		return channel.Reader.ReadAllAsync(cancellationToken);
	}

	private Task ExecuteSevenZip(string arguments, CancellationToken cancellationToken, bool useGui = false, Action<string?>? logOutput = null)
	{
		var processStartInfo = new ProcessStartInfo(!useGui ? _sevenZipPath : _sevenZipGPath, arguments)
		{
			CreateNoWindow = true,
			WorkingDirectory = Path.GetDirectoryName(_sevenZipPath),
		};

		return _processRunner.StartProcessAndAssertSuccess(processStartInfo, true, cancellationToken, logOutput);
	}

	private sealed class ArchiveListingLayout
	{
		public required Range AttributesRange { get; init; }

		public required Range SizeRange { get; init; }

		public required int PathColumnIndex { get; init; }
	}
}