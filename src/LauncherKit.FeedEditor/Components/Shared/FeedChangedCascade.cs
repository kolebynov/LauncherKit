namespace LauncherKit.FeedEditor.Components.Shared;

/// <summary>
/// Name of the cascading <see cref="Microsoft.AspNetCore.Components.EventCallback"/> that re-renders the feed editor.
/// Nested editors own their field bindings, so without it a change would not refresh summaries shown by ancestors.
/// </summary>
public static class FeedChangedCascade
{
	public const string Name = "FeedChanged";
}
