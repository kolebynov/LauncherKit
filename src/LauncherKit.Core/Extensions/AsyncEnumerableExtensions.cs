﻿namespace LauncherKit.Core.Extensions;

public static class AsyncEnumerableExtensions
{
	public static async ValueTask Enumerate<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken)
	{
		await foreach (var _ in source.WithCancellation(cancellationToken))
		{
		}
	}
}