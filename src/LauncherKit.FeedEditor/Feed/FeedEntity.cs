using System.Text.Json;
using System.Text.Json.Serialization;

namespace LauncherKit.FeedEditor.Feed;

/// <summary>
/// Base of every editable feed object. Properties the editor does not know are kept and written back unchanged,
/// so saving never drops data added by a newer launcher.
/// </summary>
public abstract class FeedEntity
{
	// The internal setter lets the source-generated serializer assign the bag while keeping it read-only for callers.
	[JsonExtensionData]
	[JsonInclude]
	public Dictionary<string, JsonElement>? UnknownProperties { get; internal set; }
}
