﻿using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

internal sealed class InstallationIdProvider : IInstallationIdProvider
{
	private const string InstallationIdKey = "InstallationId";

	private readonly IGlobalKeyValueStorage _globalKeyValueStorage;

	public InstallationIdProvider(IGlobalKeyValueStorage globalKeyValueStorage)
	{
		_globalKeyValueStorage = globalKeyValueStorage;
	}

	public string GetInstallationId()
	{
		var installationIdId = _globalKeyValueStorage.Get(InstallationIdKey);
		if (!string.IsNullOrEmpty(installationIdId))
		{
			return installationIdId;
		}

		var newInstallationIdId = Guid.NewGuid().ToString();
		_globalKeyValueStorage.Set(InstallationIdKey, newInstallationIdId);
		return newInstallationIdId;
	}
}