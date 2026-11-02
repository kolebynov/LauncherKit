namespace LauncherKit.Core.Exceptions;

/// <summary>
/// The OAuth provider did not authorize the launcher; <see cref="Error"/> is the OAuth error code
/// (<see cref="AccessDeniedError"/> when the user declined).
/// </summary>
#pragma warning disable CA1032
public sealed class OAuthAuthorizationException : Exception
{
	public const string AccessDeniedError = "access_denied";

	public string Provider { get; }

	public string Error { get; }

	public string? ErrorDescription { get; }

	public override string Message => string.IsNullOrEmpty(ErrorDescription)
		? $"{Provider} authorization failed: {Error}"
		: $"{Provider} authorization failed: {Error} ({ErrorDescription})";

	public OAuthAuthorizationException(string provider, string error, string? errorDescription)
	{
		Provider = provider;
		Error = error;
		ErrorDescription = errorDescription;
	}
}
