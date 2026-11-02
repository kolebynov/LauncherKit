﻿namespace LauncherKit.Infrastructure;

public static class SingletonExtensions
{
	extension<T>(T)
		where T : class, new()
	{
		// CA1000 discourages static members on generic types because callers must spell out the type argument;
		// here the receiver type is the type argument (`Case.Instance`), so nothing has to be spelled out.
#pragma warning disable CA1000
		public static T Instance => SimpleSingleton<T>.Value;
#pragma warning restore CA1000
	}
}