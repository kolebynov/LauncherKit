using LauncherKit.Core.Db;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

internal sealed class SkyrimLocalKeyValueStorage : ISkyrimLocalKeyValueStorage
{
	private readonly SkyrimLocalDbContext _dbContext;

	public SkyrimLocalKeyValueStorage(SkyrimLocalDbContext dbContext)
	{
		_dbContext = dbContext;
	}

	public bool HasKey(string key) => KeyValueStorageImpl.HasKey(_dbContext, key);

	public string? Get(string key) => KeyValueStorageImpl.Get(_dbContext, key);

	public void Set(string key, string? value) => KeyValueStorageImpl.Set(_dbContext, key, value);

	public void Remove(string key) => KeyValueStorageImpl.Remove(_dbContext, key);
}
