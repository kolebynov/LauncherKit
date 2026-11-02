﻿namespace LauncherKit.Core.Services.Abstractions;

public interface IKeyValueStorage
{
	bool HasKey(string key);

	string? Get(string key);

	void Set(string key, string? value);

	void Remove(string key);
}