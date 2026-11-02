using System.Runtime.InteropServices;

namespace LauncherKit.Core.Utils;

public sealed partial class NaturalStringComparer : IComparer<string>
{
	public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);

	[LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int StrCmpLogicalW(string s1, string s2);
}