using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// The owner mutex and pipe names of one product for one Windows user in one interactive session. The product is the
/// installer registry key (case-insensitive like the registry); paths, versions and display names never take part.
/// </summary>
internal readonly record struct SingleInstanceNames(string Mutex, string Pipe)
{
	public static SingleInstanceNames ForCurrentUser(string registryKey)
	{
		using var identity = WindowsIdentity.GetCurrent();
		using var process = Process.GetCurrentProcess();
		return Create(registryKey, identity.User?.Value ?? throw new InvalidOperationException("The current Windows user has no SID."),
			process.SessionId);
	}

	public static SingleInstanceNames Create(string registryKey, string userSid, int sessionId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(registryKey);
		var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRegistryKey(registryKey).ToUpperInvariant())));
		var id = $"LauncherKit-{digest}-{userSid}-{sessionId}";
		return new SingleInstanceNames(id, id);
	}

	/// <summary>
	/// The key as the .NET registry APIs open it: repeated separators after the first character collapse and a trailing
	/// separator goes, while spaces stay significant.
	/// </summary>
	private static string NormalizeRegistryKey(string registryKey)
	{
		var key = new StringBuilder(registryKey.Length);
		for (var i = 0; i < registryKey.Length; i++)
			if (i <= 1 || registryKey[i] != '\\' || registryKey[i - 1] != '\\') key.Append(registryKey[i]);
		if (key.Length > 1 && key[^1] == '\\') key.Length--;
		return key.ToString();
	}
}
