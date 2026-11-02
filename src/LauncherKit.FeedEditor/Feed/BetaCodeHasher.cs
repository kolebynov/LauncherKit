using System.Security.Cryptography;
using System.Text;

namespace LauncherKit.FeedEditor.Feed;

/// <summary>Produces the <c>BetaVersions</c> key for a beta code exactly like the launcher's <c>FeedProvider.HashBetaCode</c>.</summary>
public static class BetaCodeHasher
{
	private static readonly byte[] Salt = [10, 100, 128, 250];

	public static string Hash(string betaCode)
	{
		var byteCount = Encoding.UTF8.GetByteCount(betaCode);
		var toHash = new byte[byteCount + Salt.Length];
		Encoding.UTF8.GetBytes(betaCode, toHash);
		Salt.CopyTo(toHash, byteCount);
		return Convert.ToHexString(SHA256.HashData(toHash));
	}
}
