using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;
using Markdown.Avalonia;

namespace LauncherKit.Desktop.Controls;

/// <summary>
/// A <see cref="MarkdownScrollViewer"/> showing the document at <see cref="DocumentSource"/>, downloaded through the
/// application's <see cref="IMarkdownLoader"/> without blocking the UI thread. Never set the base <see cref="MarkdownScrollViewer.Source"/>
/// to a URL: it downloads synchronously on the UI thread and shows HTTP error pages as the document.
/// </summary>
/// <remarks>
/// The viewer loads only while it is in the visual tree and cancels an unfinished load when it leaves it. It keeps the
/// document it shows: setting the same source again, or the source passing through another value and back within the
/// same dispatcher turn (a binding whose data context is replaced), downloads nothing. When a download fails it shows
/// <see cref="DesktopStrings.MarkdownLoadFailed"/> and downloads again the next time it is shown. While
/// <see cref="DocumentSource"/> is null the viewer leaves <see cref="MarkdownScrollViewer.Markdown"/> to its owner.
/// </remarks>
// Each load disposes its cancellation source when it ends; _loading only refers to the unfinished one, which leaving
// the visual tree cancels.
#pragma warning disable CA1001
public sealed class FeedMarkdownViewer : MarkdownScrollViewer
#pragma warning restore CA1001
{
	public static readonly StyledProperty<Uri?> DocumentSourceProperty =
		AvaloniaProperty.Register<FeedMarkdownViewer, Uri?>(nameof(DocumentSource));

	private readonly Func<IMarkdownLoader> _resolveLoader;
	private Uri? _shownSource;
	private Uri? _loadingSource;
	private CancellationTokenSource? _loading;
	private bool _ownsMarkdown;
	private bool _isUpdateScheduled;

	/// <summary>Resolves the loader from the current scope of the running <see cref="LauncherKitApp"/> for each download.</summary>
	public FeedMarkdownViewer()
		: this(static () => (Application.Current as LauncherKitApp ?? throw new InvalidOperationException(
			$"{nameof(FeedMarkdownViewer)} needs a running {nameof(LauncherKitApp)} to resolve {nameof(IMarkdownLoader)}."))
			.Services.GetRequiredService<IMarkdownLoader>())
	{
	}

	internal FeedMarkdownViewer(Func<IMarkdownLoader> resolveLoader) => _resolveLoader = resolveLoader;

	/// <summary>The absolute URL of the document to show; null clears the document shown from a previous source.</summary>
	public Uri? DocumentSource
	{
		get => GetValue(DocumentSourceProperty);
		set => SetValue(DocumentSourceProperty, value);
	}

	protected override Type StyleKeyOverride => typeof(MarkdownScrollViewer);

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == DocumentSourceProperty)
		{
			ScheduleUpdate();
		}
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		ScheduleUpdate();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		CancelLoading();
	}

	private void ScheduleUpdate()
	{
		if (_isUpdateScheduled)
		{
			return;
		}

		_isUpdateScheduled = true;
		Dispatcher.UIThread.Post(Update);
	}

	private void Update()
	{
		_isUpdateScheduled = false;
		if (VisualRoot == null)
		{
			return;
		}

		var source = DocumentSource;
		if (_loading != null ? source == _loadingSource : source != null && source == _shownSource)
		{
			return;
		}

		CancelLoading();
		if (_ownsMarkdown)
		{
			_shownSource = null;
			_ownsMarkdown = false;
			Markdown = null;
		}

		if (source == null)
		{
			return;
		}

		var loader = _resolveLoader();
		_loadingSource = source;
		_loading = new CancellationTokenSource();
		Load(loader, source, _loading);
	}

	private async void Load(IMarkdownLoader loader, Uri source, CancellationTokenSource loading)
	{
		string? text;
		try
		{
			text = await loader.TryLoad(source, loading.Token);
		}
		catch (OperationCanceledException) when (loading.IsCancellationRequested)
		{
			return;
		}
		finally
		{
			if (_loading == loading)
			{
				_loading = null;
				_loadingSource = null;
			}

			loading.Dispose();
		}

		if (loading.IsCancellationRequested)
		{
			return;
		}

		if (text == null)
		{
			Markdown = LocalizationMarkup.DesktopStrings[DesktopStrings.MarkdownLoadFailed];
			_ownsMarkdown = true;
			return;
		}

		// Relative links and images resolve against the document, as with MarkdownScrollViewer.Source.
		AssetPathRoot = source.AbsoluteUri;
		Markdown = text;
		_shownSource = source;
		_ownsMarkdown = true;
	}

	private void CancelLoading()
	{
		if (_loading == null)
		{
			return;
		}

		_loading.Cancel();
		_loading = null;
		_loadingSource = null;
	}
}
