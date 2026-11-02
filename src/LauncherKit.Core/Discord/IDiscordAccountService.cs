using R3;
using LauncherKit.Core.Exceptions;

namespace LauncherKit.Core.Discord;

/// <summary>
/// The Discord account the user linked to the launcher, with its membership in the configured server.
/// </summary>
public interface IDiscordAccountService
{
	/// <summary>
	/// Raised after an account is linked or unlinked, or a link Discord no longer accepts is forgotten.
	/// </summary>
	Observable<Unit> LinkedAccountChanged { get; }

	/// <summary>
	/// Reads the linked account from Discord, refreshing the stored tokens when they are about to expire.
	/// </summary>
	/// <returns>
	/// <see langword="null"/> when no account is linked, or when Discord no longer accepts the link (the user revoked
	/// it in Discord), which then forgets the link.
	/// </returns>
	/// <exception cref="HttpRequestException">Discord could not be reached.</exception>
	Task<DiscordAccount?> GetLinkedAccount(CancellationToken cancellationToken);

	/// <summary>
	/// Like <see cref="GetLinkedAccount"/>, but serves the last read for a few minutes (a failed read for a minute,
	/// rethrowing its exception), for callers that read the account often, such as feed filtering.
	/// </summary>
	Task<DiscordAccount?> GetCachedLinkedAccount(CancellationToken cancellationToken);

	/// <summary>
	/// Opens the Discord authorization page in the browser and links the account the user authorizes, replacing
	/// a previously linked one.
	/// </summary>
	/// <exception cref="OAuthAuthorizationException">The user declined or Discord rejected the authorization.</exception>
	/// <exception cref="System.Net.Sockets.SocketException">The redirect port is in use.</exception>
	Task<DiscordAccount> Link(CancellationToken cancellationToken);

	/// <summary>
	/// Forgets the linked account and revokes its authorization on Discord; a failed revocation is only logged,
	/// since the launcher forgets the tokens anyway.
	/// </summary>
	Task Unlink(CancellationToken cancellationToken);
}
