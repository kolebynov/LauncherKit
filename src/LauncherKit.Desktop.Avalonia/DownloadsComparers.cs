using System.Collections;
using LauncherKit.Desktop.ViewModels;

namespace LauncherKit.Desktop.Avalonia;

public static class DownloadsComparers
{
	// Explicit comparers keep column sorting independent of reflection under NativeAOT.
	public static IComparer FileNameComparer { get; } = Comparer<DownloadsViewModel.FileInfoViewModel>.Create(
		(x, y) => string.Compare(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase));
	public static IComparer SizeComparer { get; } = Comparer<DownloadsViewModel.FileInfoViewModel>.Create((x, y) => x.Size.CompareTo(y.Size));
	public static IComparer StatusComparer { get; } = Comparer<DownloadsViewModel.FileInfoViewModel>.Create((x, y) => x.Status.CompareTo(y.Status));
}
