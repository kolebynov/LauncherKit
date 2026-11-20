using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LauncherKit.Desktop.Interop;

/// <summary>Win32 calls letting a relaunch hand the foreground to the running launcher.</summary>
internal static partial class SingleInstanceNativeMethods
{
	private const int SwRestore = 9;

	/// <summary>Lets the process serving <paramref name="pipe"/> take the foreground; Windows may still refuse it.</summary>
	public static void AllowPipeServerForeground(SafePipeHandle pipe)
	{
		if (GetNamedPipeServerProcessId(pipe, out var processId)) AllowSetForegroundWindow(processId);
	}

	/// <summary>Restores a minimized window to its previous (normal or maximized) state.</summary>
	public static void RestoreIfMinimized(nint window)
	{
		if (IsIconic(window)) ShowWindow(window, SwRestore);
	}

	[LibraryImport("kernel32.dll")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

	[LibraryImport("user32.dll")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool AllowSetForegroundWindow(uint processId);

	[LibraryImport("user32.dll")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsIconic(nint window);

	[LibraryImport("user32.dll")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ShowWindow(nint window, int command);
}
