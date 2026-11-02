﻿namespace LauncherKit.Core.Models;

public sealed class InstallStepContext
{
	private readonly Dictionary<string, string> _values = new();

	public string? GetValue(string key) => _values.GetValueOrDefault(key);

	public void SetValue(string key, string value) => _values[key] = value;
}