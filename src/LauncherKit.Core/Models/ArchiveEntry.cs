namespace LauncherKit.Core.Models;

/// <summary>A file or directory inside an archive.</summary>
/// <param name="Path">Path relative to the archive root.</param>
/// <param name="Size">Uncompressed size in bytes; 0 for a directory.</param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
public readonly record struct ArchiveEntry(string Path, long Size, bool IsDirectory);
