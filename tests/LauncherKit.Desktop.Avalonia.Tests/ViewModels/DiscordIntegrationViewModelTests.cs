using Microsoft.Extensions.Logging;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Desktop.Services;
using LauncherKit.Core.Discord;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.Avalonia.Tests.ViewModels;

[TestFixture]
public sealed class DiscordIntegrationViewModelTests
{
	private static readonly DiscordAccount Account = new(
		new DiscordUser(1, "nelly", "Nelly", new Uri("https://cdn.discordapp.com/embed/avatars/0.png")),
		new DiscordGuildMember(null, null, [197038439483310086, 41771983423143936]));

	[Test]
	public async Task Activate_SponsorAccountLinked_ShowsAccountAndSponsorRoleFoundAndOffersUnlinking()
	{
		// Arrange
		var mocker = CreateMocker(197038439483310086);
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Account);
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Account!.DisplayName, Is.EqualTo("Nelly"));
			Assert.That(sut.Membership, Is.EqualTo(DiscordMembership.Sponsor));
			Assert.That(sut.CanUnlink, Is.True);
			Assert.That(sut.CanLink, Is.False);
		});
	}

	[Test]
	public async Task Activate_MemberWithoutSponsorRole_ShowsMembershipOnly()
	{
		// Arrange
		var mocker = CreateMocker(1);
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Account);
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.That(sut.Membership, Is.EqualTo(DiscordMembership.Member));
	}

	[Test]
	public async Task Activate_MemberWithDeveloperRole_ShowsDeveloperRoleFound()
	{
		// Arrange
		var mocker = CreateMocker(new BetaAccessRoles([197038439483310086], [41771983423143936]));
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Account);
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.That(sut.Membership, Is.EqualTo(DiscordMembership.Developer));
	}

	[Test]
	public async Task Activate_MemberWithServerProfile_ShowsServerNicknameAndAvatar()
	{
		// Arrange
		var serverAvatar = new Uri("https://cdn.discordapp.com/guilds/42/users/1/avatars/a1b2c3.png?size=128");
		var mocker = CreateMocker();
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Account with { GuildMember = new DiscordGuildMember("Dovahkiin", serverAvatar, []) });
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Account!.DisplayName, Is.EqualTo("Dovahkiin"));
			Assert.That(sut.Account!.AvatarUri, Is.EqualTo(serverAvatar));
		});
	}

	[Test]
	public async Task Activate_LoadFails_OffersRetryInsteadOfLinking()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException());
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Error, Is.InstanceOf<HttpRequestException>());
			Assert.That(sut.CanRetryLoading, Is.True);
			Assert.That(sut.CanLink, Is.False);
		});
	}

	[Test]
	public async Task LinkCommand_UserDeclines_ShowsErrorAndOffersLinkingAgain()
	{
		// Arrange
		var mocker = CreateMocker();
		var service = mocker.GetMock<IDiscordAccountService>();
		service.Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>())).ReturnsAsync((DiscordAccount?)null);
		service.Setup(x => x.Link(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new OAuthAuthorizationException("Discord", OAuthAuthorizationException.AccessDeniedError, null));
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Act
		await sut.LinkCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Error, Is.InstanceOf<OAuthAuthorizationException>());
			Assert.That(sut.IsLinking, Is.False);
			Assert.That(sut.CanLink, Is.True);
		});
	}

	[Test]
	public async Task Deactivate_WhileLinking_CancelsLinking()
	{
		// Arrange
		var mocker = CreateMocker();
		var service = mocker.GetMock<IDiscordAccountService>();
		service.Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>())).ReturnsAsync((DiscordAccount?)null);
		service.Setup(x => x.Link(It.IsAny<CancellationToken>()))
			.Returns(async (CancellationToken ct) =>
			{
				await Task.Delay(Timeout.Infinite, ct);
				return Account;
			});
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();
		var activation = await sut.Activator.Activate(CancellationToken.None);
		var linking = sut.LinkCommand.ExecuteAsync(null);

		// Act
		activation.Dispose();
		await linking;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsLinking, Is.False);
			Assert.That(sut.Error, Is.Null);
			Assert.That(sut.Account, Is.Null);
		});
	}

	[Test]
	public async Task UnlinkCommand_Confirmed_UnlinksAndOffersLinking()
	{
		// Arrange
		var mocker = CreateMocker();
		var service = mocker.GetMock<IDiscordAccountService>();
		service.Setup(x => x.GetLinkedAccount(It.IsAny<CancellationToken>())).ReturnsAsync(Account);
		service.Setup(x => x.Unlink(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<DiscordIntegrationViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Act
		await sut.UnlinkCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Account, Is.Null);
			Assert.That(sut.CanLink, Is.True);
		});
		service.Verify(x => x.Unlink(It.IsAny<CancellationToken>()), Times.Once);
	}

	private static AutoMocker CreateMocker(params ulong[] sponsorRoleIds) =>
		CreateMocker(new BetaAccessRoles(sponsorRoleIds, []));

	private static AutoMocker CreateMocker(BetaAccessRoles betaAccessRoles)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<DiscordIntegrationViewModel>>(NullLogger<DiscordIntegrationViewModel>.Instance);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetBetaAccessRoles(It.IsAny<CancellationToken>()))
			.ReturnsAsync(betaAccessRoles);
		return mocker;
	}
}
