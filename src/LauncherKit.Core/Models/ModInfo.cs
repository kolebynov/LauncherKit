﻿namespace LauncherKit.Core.Models;

public sealed class ModInfo
{
	private readonly Lazy<MetaIniData?> _metaIniLazy;

	public string Name { get; }

	public string? Description => _metaIniLazy.Value?.Comments;

	public string? Version => _metaIniLazy.Value?.Version;

	public ModInfo(string name, Lazy<MetaIniData?> metaIniLazy)
	{
		Name = name;
		_metaIniLazy = metaIniLazy;
	}
}