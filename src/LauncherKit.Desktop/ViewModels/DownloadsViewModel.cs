using System.Collections.ObjectModel;
using LauncherKit.Desktop.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Core.FileStorage;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class DownloadsViewModel : BaseActivatableViewModel
{
	private readonly IFileStorageService _fileStorageService;

	public ObservableCollection<FileInfoViewModel> Files { get; } = new();

	[ObservableProperty]
	public partial IReadOnlyList<FileInfoViewModel> SelectedFiles { get; set; } = new List<FileInfoViewModel>();

	public DownloadsViewModel(IFileStorageService fileStorageService)
	{
		_fileStorageService = fileStorageService;
		this.WhenActivated(_ =>
		{
			Files.Clear();
			Files.AddRange(fileStorageService.GetFileInfos().Select(x => new FileInfoViewModel
			{
				FileName = x.FileName,
				Size = x.Size,
				Status = x.Status,
			}));
		});
	}

	/// <summary>Returns the storage outcome for each selected file; unsuccessful files remain in the list.</summary>
	public IReadOnlyList<FileDeleteResult> DeleteSelectedFiles() => DeleteFiles(SelectedFiles.ToArray());

	/// <inheritdoc cref="DeleteSelectedFiles"/>
	public IReadOnlyList<FileDeleteResult> DeleteAllFiles() => DeleteFiles(Files.ToArray());

	private IReadOnlyList<FileDeleteResult> DeleteFiles(IReadOnlyList<FileInfoViewModel> files)
	{
		var results = new List<FileDeleteResult>(files.Count);
		foreach (var file in files)
		{
			var result = _fileStorageService.Delete(file.FileName);
			results.Add(new FileDeleteResult(file.FileName, result));
			if (result is IFileStorageService.DeleteResult.Deleted)
			{
				Files.Remove(file);
			}
		}

		return results;
	}

	public sealed record FileDeleteResult(string FileName, IFileStorageService.DeleteResult Result);

	public sealed class FileInfoViewModel : ViewModelBase
	{
		public required string FileName { get; init; }
		public required long Size { get; init; }
		public required StoredFileStatus Status { get; init; }
	}
}
