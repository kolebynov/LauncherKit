using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

internal sealed class GlobalKeyValueStorage : IGlobalKeyValueStorage
{
	private readonly IGlobalScope _globalScope;

	public GlobalKeyValueStorage(IGlobalScope globalScope)
	{
		_globalScope = globalScope;
	}

	public bool HasKey(string key)
	{
		using var scope = _globalScope.EnterScope();
		return KeyValueStorageImpl.HasKey(scope.GlobalDbContext, key);
	}

	public string? Get(string key)
	{
		using var scope = _globalScope.EnterScope();
		return KeyValueStorageImpl.Get(scope.GlobalDbContext, key);
	}

	public void Set(string key, string? value)
	{
		using var scope = _globalScope.EnterScope();
		KeyValueStorageImpl.Set(scope.GlobalDbContext, key, value);
	}

	public void Remove(string key)
	{
		using var scope = _globalScope.EnterScope();
		KeyValueStorageImpl.Remove(scope.GlobalDbContext, key);
	}
}
