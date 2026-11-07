using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using LauncherKit.Desktop.Controls;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Tests.Controls;

[NonParallelizable]
[TestFixture]
public sealed class FeedMarkdownViewerTests
{
	private static readonly Uri First = new("https://feed.test/Changelogs/first.md");
	private static readonly Uri Second = new("https://feed.test/Changelogs/second.md");

	[AvaloniaTest]
	public void DocumentSource_Shown_ShowsDownloadedDocumentOnceLoaded()
	{
		// Arrange
		var loader = new ControlledLoader();
		var (window, viewer) = Show(loader);

		// Act
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();
		var markdownWhileLoading = viewer.Markdown;
		loader.Complete(0, "# First");
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(markdownWhileLoading, Is.Null);
			Assert.That(loader.Sources, Is.EqualTo(new[] { First }));
			Assert.That(viewer.Markdown, Is.EqualTo("# First"));
			Assert.That(viewer.AssetPathRoot, Is.EqualTo(First.AbsoluteUri));
		});
		window.Close();
	}

	[AvaloniaTest]
	public void DocumentSource_ChangedWhileLoading_CancelsEarlierLoadAndShowsLatest()
	{
		// Arrange
		var loader = new ControlledLoader();
		var (window, viewer) = Show(loader);
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();

		// Act
		viewer.DocumentSource = Second;
		Dispatcher.UIThread.RunJobs();
		loader.Complete(1, "# Second");
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(loader.Tokens[0].IsCancellationRequested, Is.True);
			Assert.That(viewer.Markdown, Is.EqualTo("# Second"));
		});
		window.Close();
	}

	[AvaloniaTest]
	public void DocumentSource_ShownAgain_DoesNotDownloadShownDocumentAgain()
	{
		// Arrange
		var loader = new ControlledLoader();
		var (window, viewer) = Show(loader);
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();
		loader.Complete(0, "# First");
		Dispatcher.UIThread.RunJobs();

		// Act
		window.Content = null;
		Dispatcher.UIThread.RunJobs();
		window.Content = viewer;
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(loader.Sources, Has.Count.EqualTo(1));
			Assert.That(viewer.Markdown, Is.EqualTo("# First"));
		});
		window.Close();
	}

	[AvaloniaTest]
	public void DocumentSource_ClearedAndRestoredInOneTurn_DoesNotDownloadAgain()
	{
		// Arrange
		var loader = new ControlledLoader();
		var (window, viewer) = Show(loader);
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();
		loader.Complete(0, "# First");
		Dispatcher.UIThread.RunJobs();

		// Act
		viewer.DocumentSource = null;
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(loader.Sources, Has.Count.EqualTo(1));
			Assert.That(viewer.Markdown, Is.EqualTo("# First"));
		});
		window.Close();
	}

	[AvaloniaTest]
	public void DocumentSource_ClearedAfterLoad_ClearsDocument()
	{
		// Arrange
		var loader = new ControlledLoader();
		var (window, viewer) = Show(loader);
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();
		loader.Complete(0, "# First");
		Dispatcher.UIThread.RunJobs();

		// Act
		viewer.DocumentSource = null;
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.That(viewer.Markdown, Is.Null);
		window.Close();
	}

	[AvaloniaTest]
	public void DocumentSource_DownloadFails_ShowsFailureAndDownloadsAgainWhenShownAgain()
	{
		// Arrange
		var loader = new ControlledLoader();
		var (window, viewer) = Show(loader);
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();

		// Act
		loader.Complete(0, null);
		Dispatcher.UIThread.RunJobs();
		var markdownAfterFailure = viewer.Markdown;
		window.Content = null;
		Dispatcher.UIThread.RunJobs();
		window.Content = viewer;
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(markdownAfterFailure, Is.EqualTo(LocalizationMarkup.DesktopStrings[DesktopStrings.MarkdownLoadFailed].Value));
			Assert.That(loader.Sources, Is.EqualTo(new[] { First, First }));
		});
		window.Close();
	}

	[AvaloniaTest]
	public void DocumentSource_HiddenWhileLoading_CancelsDownload()
	{
		// Arrange
		var loader = new ControlledLoader();
		var (window, viewer) = Show(loader);
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();

		// Act
		window.Content = null;
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(loader.Tokens[0].IsCancellationRequested, Is.True);
			Assert.That(viewer.Markdown, Is.Null);
		});
		window.Close();
	}

	[AvaloniaTest]
	public void DocumentSource_NotShown_DoesNotDownload()
	{
		// Arrange
		var loader = new ControlledLoader();
		var viewer = new FeedMarkdownViewer(() => loader);

		// Act
		viewer.DocumentSource = First;
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.That(loader.Sources, Is.Empty);
	}

	[AvaloniaTest]
	public void Markdown_SetByOwnerWithoutDocumentSource_IsKept()
	{
		// Arrange
		var loader = new ControlledLoader();
		var viewer = new FeedMarkdownViewer(() => loader) { Markdown = "Owner text" };

		// Act
		var window = new Window { Content = viewer };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(viewer.Markdown, Is.EqualTo("Owner text"));
			Assert.That(loader.Sources, Is.Empty);
		});
		window.Close();
	}

	private static (Window Window, FeedMarkdownViewer Viewer) Show(IMarkdownLoader loader)
	{
		var viewer = new FeedMarkdownViewer(() => loader);
		var window = new Window { Content = viewer };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, viewer);
	}

	/// <summary>Loader whose downloads finish only when the test completes them.</summary>
	private sealed class ControlledLoader : IMarkdownLoader
	{
		private readonly List<TaskCompletionSource<string?>> _results = [];

		public List<Uri> Sources { get; } = [];

		public List<CancellationToken> Tokens { get; } = [];

		public Task<string?> TryLoad(Uri source, CancellationToken cancellationToken)
		{
			var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
			cancellationToken.Register(() => result.TrySetCanceled(cancellationToken));
			Sources.Add(source);
			Tokens.Add(cancellationToken);
			_results.Add(result);
			return result.Task;
		}

		public void Complete(int index, string? text) => _results[index].TrySetResult(text);
	}
}
