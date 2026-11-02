﻿namespace LauncherKit.Core.Initialization;

public interface IEarlyConfiguredScopeInitializer
{
	ValueTask Initialize();
}