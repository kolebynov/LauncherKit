namespace LauncherKit.Desktop.ViewModels;

public sealed class SelectDirectoryViewModel : ViewModelBase
{
	public string Title { get; }

	public DirectoryBrowserViewModel DirectoryBrowserViewModel { get; }

	public SelectDirectoryViewModel(DirectoryBrowserViewModel directoryBrowserViewModel, string title)
	{
		DirectoryBrowserViewModel = directoryBrowserViewModel;
		Title = title;
	}
}