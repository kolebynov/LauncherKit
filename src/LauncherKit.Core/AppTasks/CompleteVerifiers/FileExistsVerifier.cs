﻿namespace LauncherKit.Core.AppTasks.CompleteVerifiers;

internal sealed class FileExistsVerifier : IAppTaskCompleteVerifier
{
	private readonly string _path;
	private readonly bool _invert;

	public FileExistsVerifier(string path, bool invert = false)
	{
		_path = path;
		_invert = invert;
	}

	public ValueTask<bool> IsTaskComplete(CancellationToken cancellationToken)
	{
		var exists = File.Exists(_path);
		return ValueTask.FromResult(_invert ? !exists : exists);
	}
}