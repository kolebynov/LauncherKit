using System.Buffers;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

public enum DirectoryValidationError
{
	InvalidPath,
	ConflictingDirectory,
	SkyrimDirectoryConflict,
	AppDataDirectoryConflict,
	NonAsciiSkyrimPath,
}

public class DirectoryBrowserViewModel : ViewModelBase, INotifyDataErrorInfo
{
	private static readonly SearchValues<char> InvalidPathChars = SearchValues.Create(Path.GetInvalidPathChars());
	private DirectoryValidationError? _selectedDirectoryError;

	public string? SelectedDirectory
	{
		get;
		set { if (SetProperty(ref field, value)) UpdateErrors(); }
	}

	public string? ConflictingDirectory
	{
		get;
		set { if (SetProperty(ref field, value)) UpdateErrors(); }
	}

	public virtual void SelectDirectory(string directory) => SelectedDirectory = directory;

	public DirectoryValidationError ConflictingDirectoryError { get; init; } = DirectoryValidationError.ConflictingDirectory;

	public bool HasErrors => _selectedDirectoryError != null;

	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

	public IEnumerable<DirectoryValidationError> GetErrors(string? propertyName) =>
		_selectedDirectoryError is { } error &&
		(string.IsNullOrEmpty(propertyName) || propertyName == nameof(SelectedDirectory)) ? [error] : [];

	IEnumerable INotifyDataErrorInfo.GetErrors(string? propertyName) => GetErrors(propertyName);

	private void UpdateErrors()
	{
		var previous = _selectedDirectoryError;
		_selectedDirectoryError = !IsValidPath(SelectedDirectory) ? DirectoryValidationError.InvalidPath :
			IsValidPath(ConflictingDirectory) && PathUtils.AreSameOrNested(SelectedDirectory, ConflictingDirectory)
				? ConflictingDirectoryError : ValidateSelectedDirectory(SelectedDirectory);
		if ((previous != null) != HasErrors) OnPropertyChanged(nameof(HasErrors));
		if (previous != _selectedDirectoryError)
			ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(SelectedDirectory)));
	}

	private static bool IsValidPath([NotNullWhen(true)] string? path) =>
		!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && !path.AsSpan().ContainsAny(InvalidPathChars);

	protected virtual DirectoryValidationError? ValidateSelectedDirectory(string selectedDirectory) => null;
}
