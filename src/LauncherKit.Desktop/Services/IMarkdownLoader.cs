namespace LauncherKit.Desktop.Services;

/// <summary>Downloads Markdown documents (changelogs, descriptions) from the feed server.</summary>
public interface IMarkdownLoader
{
	/// <summary>
	/// Returns the document text, or null when it could not be downloaded, including an unsuccessful HTTP status
	/// (the failure is logged). Throws <see cref="OperationCanceledException"/> only when
	/// <paramref name="cancellationToken"/> is cancelled.
	/// </summary>
	Task<string?> TryLoad(Uri source, CancellationToken cancellationToken);
}
