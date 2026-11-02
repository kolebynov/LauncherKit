namespace LauncherKit.FeedEditor.Components.Shared;

/// <summary>One way to create a new entry in a <see cref="ListEditor{TItem}"/>, shown as an "Add" button or menu item.</summary>
public sealed record ListItemFactory<TItem>(string Label, Func<TItem> Create);
