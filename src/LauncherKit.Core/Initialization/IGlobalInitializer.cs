﻿namespace LauncherKit.Core.Initialization;

public interface IGlobalInitializer
{
	ValueTask Initialize();
}