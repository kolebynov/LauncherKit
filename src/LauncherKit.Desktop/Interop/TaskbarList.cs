using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LauncherKit.Desktop.Interop;

/// <summary>
/// Creates the shell's <c>TaskbarList</c> COM object through source-generated COM interop.
/// </summary>
public static partial class TaskbarList
{
	internal const string TaskbarList3Iid = "ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf";

	private const uint ClsctxInprocServer = 0x1;

	private static readonly Guid TaskbarListClsid = new("56FDF344-FD6D-11d0-958A-006097C9A090");
	private static readonly Guid TaskbarList3Guid = new(TaskbarList3Iid);
	private static readonly StrategyBasedComWrappers ComWrappers = new();

	/// <summary>
	/// Creates the object and calls <see cref="ITaskbarList3.HrInit"/>.
	/// </summary>
	public static ITaskbarList3 Create()
	{
		Marshal.ThrowExceptionForHR(
			CoCreateInstance(TaskbarListClsid, 0, ClsctxInprocServer, TaskbarList3Guid, out var instance));
		ITaskbarList3 taskbarList;
		try
		{
			taskbarList = (ITaskbarList3)ComWrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.None);
		}
		finally
		{
			Marshal.Release(instance);
		}

		taskbarList.HrInit();
		return taskbarList;
	}

	[LibraryImport("ole32.dll")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int CoCreateInstance(
		in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
}
