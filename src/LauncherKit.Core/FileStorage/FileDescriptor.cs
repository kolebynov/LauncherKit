namespace LauncherKit.Core.FileStorage;

public union FileDescriptor(FileDescriptor.Opened, FileDescriptor.Finished) : IAsyncDisposable
{
	public sealed record Opened(OpenedFile OpenedFile);

	public sealed record Finished(FileStream FileStream);

	public ValueTask DisposeAsync() =>
		this switch
		{
			Opened(var openedFile) => openedFile.DisposeAsync(),
			Finished(var fileStream) => fileStream.DisposeAsync(),
		};
}