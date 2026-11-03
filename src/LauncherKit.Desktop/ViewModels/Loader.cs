using CommunityToolkit.Mvvm.ComponentModel;
using R3;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>Tracks overlapping loading operations on the UI thread.</summary>
public partial class Loader : ViewModelBase
{
	private int _loadingCount;

	[ObservableProperty]
	public partial bool IsLoading { get; private set; }

	public IDisposable BeginLoading()
	{
		_loadingCount++;
		IsLoading = true;
		return Disposable.Create(this, static loader => loader.IsLoading = --loader._loadingCount > 0);
	}
}
