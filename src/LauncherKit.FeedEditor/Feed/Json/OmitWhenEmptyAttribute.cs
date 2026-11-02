namespace LauncherKit.FeedEditor.Feed.Json;

/// <summary>
/// Marks a collection the launcher treats the same whether it is empty, <c>null</c> or missing. The editor reads all
/// three as empty and omits an empty one when writing, so feeds that leave it out are written back unchanged.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OmitWhenEmptyAttribute : Attribute;
