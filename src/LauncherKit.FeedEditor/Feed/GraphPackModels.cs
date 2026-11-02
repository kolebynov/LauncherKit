namespace LauncherKit.FeedEditor.Feed;

/// <summary>
/// A graph pack: a mod (installed like an optional mod, its install steps extracting further archives or switching to
/// its profile) plus what the modpack installation needs to offer it.
/// </summary>
public sealed class GraphPackModel : ModModel
{
	/// <summary>Shown in the graph pack's option when installing modpack.</summary>
	public Dictionary<string, string> Recommendation { get; init => field = value ?? []; } = [];

	/// <summary>Selected by default when installing modpack.</summary>
	public bool IsDefault { get; set; }

	/// <summary>
	/// No new versions will be released: the launcher hides the graph pack for modpack versions none of its versions
	/// supports instead of showing it as being updated.
	/// </summary>
	public bool IsDiscontinued { get; set; }
}
