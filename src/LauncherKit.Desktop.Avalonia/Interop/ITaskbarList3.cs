using System.Runtime.InteropServices;
using LauncherKit.Desktop.Avalonia.Interop;
using System.Runtime.InteropServices.Marshalling;

namespace LauncherKit.Desktop.Avalonia.Interop;

[GeneratedComInterface]
[Guid(TaskbarList.TaskbarList3Iid)]
public partial interface ITaskbarList3
{
	// ITaskbarList
	void HrInit();

	void AddTab(nint hwnd);

	void DeleteTab(nint hwnd);

	void ActivateTab(nint hwnd);

	void SetActiveAlt(nint hwnd);

	// ITaskbarList2
	void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

	// ITaskbarList3
	void SetProgressValue(nint hwnd, ulong ullCompleted, ulong ullTotal);

	void SetProgressState(nint hwnd, TaskbarProgressBarState state);
}
