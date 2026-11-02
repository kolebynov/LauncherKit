using System.Text;
using Microsoft.Extensions.Options;
using LauncherKit.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.OAuth;

internal sealed class ProtectedOAuthTokenStore : IOAuthTokenStore
{
	private readonly byte[] _entropy;

	private readonly IGlobalKeyValueStorage _storage;

	public ProtectedOAuthTokenStore(IGlobalKeyValueStorage storage, IOptions<LauncherProductOptions> product)
	{
		product.Value.Validate();
		_storage = storage;
		_entropy = Encoding.UTF8.GetBytes(product.Value.OAuthTokenEntropy);
	}

	public OAuthToken? Get(string provider)
	{
		if (_storage.Get(GetKey(provider)) is not { } value)
		{
			return null;
		}

		var json = ProtectedData.Unprotect(Convert.FromBase64String(value), _entropy, DataProtectionScope.CurrentUser);
		return JsonSerializer.Deserialize(json, OAuthJsonContext.Default.OAuthToken) ??
		       throw new InvalidOperationException($"Stored {provider} OAuth token is null");
	}

	public void Set(string provider, OAuthToken token)
	{
		var json = JsonSerializer.SerializeToUtf8Bytes(token, OAuthJsonContext.Default.OAuthToken);
		var protectedJson = ProtectedData.Protect(json, _entropy, DataProtectionScope.CurrentUser);
		_storage.Set(GetKey(provider), Convert.ToBase64String(protectedJson));
	}

	public void Remove(string provider) => _storage.Remove(GetKey(provider));

	private static string GetKey(string provider) => $"OAuthToken_{provider}";
}
