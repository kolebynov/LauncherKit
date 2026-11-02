namespace LauncherKit.Core.Models;

/// <summary>The outcome of deleting old saves.</summary>
/// <param name="Count">The number of deleted saves.</param>
/// <param name="FreedBytes">The total size of the deleted save files and co-saves.</param>
/// <param name="Error">
/// Why the deletion stopped before every old save was deleted, or <see langword="null"/> when it finished; the
/// counts then cover only the saves deleted before it.
/// </param>
public readonly record struct DeletedSavesInfo(int Count, long FreedBytes, Exception? Error);
