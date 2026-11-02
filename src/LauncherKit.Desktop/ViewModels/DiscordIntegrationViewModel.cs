using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3;
using LauncherKit.Desktop.Services;
using LauncherKit.Core.Discord;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class DiscordIntegrationViewModel : BaseActivatableViewModel
{
	private readonly IDiscordAccountService _discordAccountService;
	private readonly IFeedProvider _feedProvider;
	private readonly ILogger<DiscordIntegrationViewModel> _logger;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsLinked), nameof(Membership), nameof(CanLink),
		nameof(CanUnlink))]
	public partial DiscordAccount? Account { get; private set; }

	/// <summary>
	/// Whether the linked account is being read or unlinked.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanLink), nameof(CanUnlink), nameof(CanRetryLoading))]
	public partial bool IsBusy { get; private set; }

	/// <summary>
	/// Whether the user is authorizing the launcher in the browser.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanLink))]
	public partial bool IsLinking { get; private set; }

	/// <summary>
	/// Whether reading the linked account failed, so it is unknown whether one is linked.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanLink), nameof(CanRetryLoading))]
	public partial bool IsLoadFailed { get; private set; }

	[ObservableProperty]
	public partial Exception? Error { get; private set; }

	/// <summary>
	/// The feed's sponsor and developer roles, read with the account; empty when the feed lists none or could not be
	/// loaded.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Membership))]
	public partial BetaAccessRoles BetaAccessRoles { get; private set; } = BetaAccessRoles.None;

	public bool IsLinked => Account != null;

	public DiscordMembership? Membership => Account switch
	{
		null => null,
		{ GuildMember: null } => DiscordMembership.NotMember,
		_ when Account.HasAnyRole(BetaAccessRoles.DeveloperRoleIds) => DiscordMembership.Developer,
		_ when Account.HasAnyRole(BetaAccessRoles.SponsorRoleIds) => DiscordMembership.Sponsor,
		_ => DiscordMembership.Member,
	};

	public bool CanLink => !IsLinked && !IsBusy && !IsLinking && !IsLoadFailed;

	public bool CanUnlink => IsLinked && !IsBusy;

	public bool CanRetryLoading => IsLoadFailed && !IsBusy;

	public DiscordIntegrationViewModel(IDiscordAccountService discordAccountService,
		IFeedProvider feedProvider, ILogger<DiscordIntegrationViewModel> logger)
	{
		_discordAccountService = discordAccountService;
		_feedProvider = feedProvider;
		_logger = logger;

		this.WhenActivated(async (d, ct) =>
		{
			// Leaving the page stops waiting for the browser and frees the redirect port.
			Disposable.Create(() => LinkCommand.Cancel()).AddTo(d);
			await LoadAccount(ct);
		});
	}

	[RelayCommand]
	private Task RetryLoading() => LoadAccount(CancellationToken.None);

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Link(CancellationToken cancellationToken)
	{
		Error = null;
		IsLinking = true;
		try
		{
			var account = await _discordAccountService.Link(cancellationToken);
			BetaAccessRoles = await _feedProvider.GetBetaAccessRoles(cancellationToken);
			Account = account;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to link a Discord account");
			Error = e;
		}
		finally
		{
			IsLinking = false;
		}
	}

	[RelayCommand]
	private async Task Unlink()
	{
		Error = null;
		IsBusy = true;
		try
		{
			await _discordAccountService.Unlink(CancellationToken.None);
			Account = null;
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to unlink the Discord account");
			Error = e;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task LoadAccount(CancellationToken cancellationToken)
	{
		Error = null;
		IsLoadFailed = false;
		IsBusy = true;
		try
		{
			var account = await _discordAccountService.GetLinkedAccount(cancellationToken);
			BetaAccessRoles = await _feedProvider.GetBetaAccessRoles(cancellationToken);
			Account = account;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to read the linked Discord account");
			Error = e;
			IsLoadFailed = true;
		}
		finally
		{
			IsBusy = false;
		}
	}
}

public enum DiscordMembership { NotMember, Member, Sponsor, Developer }
