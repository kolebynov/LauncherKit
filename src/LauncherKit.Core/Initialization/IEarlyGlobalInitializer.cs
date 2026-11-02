﻿namespace LauncherKit.Core.Initialization;

public interface IEarlyGlobalInitializer
{
	ValueTask Initialize();
}