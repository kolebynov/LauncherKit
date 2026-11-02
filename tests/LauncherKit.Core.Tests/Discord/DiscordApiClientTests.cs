using System.Net;
using RichardSzalay.MockHttp;
using LauncherKit.Core.Discord;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Discord;

[TestFixture]
public sealed class DiscordApiClientTests
{
	private const string UserUrl = "https://discord.com/api/v10/users/@me";
	private const string MemberUrl = "https://discord.com/api/v10/users/@me/guilds/42/member";

	[Test]
	public async Task GetCurrentUser_UserWithAvatarAndGlobalName_SendsBearerTokenAndMapsUser()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, UserUrl)
			.WithHeaders("Authorization", "Bearer access")
			.Respond("application/json",
				"""{"id":"80351110224678912","username":"nelly","global_name":"Nelly","avatar":"8342729096ea3675442027381ff50dfe"}""");
		var sut = CreateSut(handler);

		// Act
		var user = await sut.GetCurrentUser("access", CancellationToken.None);

		// Assert
		Assert.That(user, Is.EqualTo(new DiscordUser(80351110224678912, "nelly", "Nelly",
			new Uri("https://cdn.discordapp.com/avatars/80351110224678912/8342729096ea3675442027381ff50dfe.png?size=128"))));
	}

	[Test]
	public async Task GetCurrentUser_UserWithoutAvatarAndGlobalName_UsesUserNameAndDefaultAvatar()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, UserUrl)
			.Respond("application/json", """{"id":"80351110224678912","username":"nelly","global_name":null,"avatar":null}""");
		var sut = CreateSut(handler);

		// Act
		var user = await sut.GetCurrentUser("access", CancellationToken.None);

		// Assert
		// (80351110224678912 >> 22) % 6 == 5
		Assert.That(user, Is.EqualTo(new DiscordUser(80351110224678912, "nelly", "nelly",
			new Uri("https://cdn.discordapp.com/embed/avatars/5.png"))));
	}

	[Test]
	public void GetCurrentUser_TokenRejected_ThrowsUnauthorizedHttpRequestException()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, UserUrl).Respond(HttpStatusCode.Unauthorized);
		var sut = CreateSut(handler);

		// Act
		// Assert
		Assert.That(async () => await sut.GetCurrentUser("access", CancellationToken.None),
			Throws.InstanceOf<HttpRequestException>()
				.With.Property(nameof(HttpRequestException.StatusCode)).EqualTo(HttpStatusCode.Unauthorized));
	}

	[Test]
	public async Task GetCurrentUserGuildMember_MemberWithServerProfile_ReturnsNicknameServerAvatarAndRoleIds()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, MemberUrl)
			.WithHeaders("Authorization", "Bearer access")
			.Respond("application/json",
				"""{"nick":"Dovahkiin","avatar":"a1b2c3","roles":["197038439483310086","41771983423143936"],"joined_at":"2015-04-26T06:26:56.936000+00:00"}""");
		var sut = CreateSut(handler);

		// Act
		var member = await sut.GetCurrentUserGuildMember("access", 42, 80351110224678912, CancellationToken.None);

		// Assert
		Assert.That(member, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(member!.Nickname, Is.EqualTo("Dovahkiin"));
			Assert.That(member.AvatarUri,
				Is.EqualTo(new Uri("https://cdn.discordapp.com/guilds/42/users/80351110224678912/avatars/a1b2c3.png?size=128")));
			Assert.That(member.RoleIds, Is.EqualTo(new ulong[] { 197038439483310086, 41771983423143936 }));
		});
	}

	[Test]
	public async Task GetCurrentUserGuildMember_MemberWithoutServerProfile_ReturnsNoNicknameOrServerAvatar()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, MemberUrl)
			.Respond("application/json", """{"nick":null,"avatar":null,"roles":[]}""");
		var sut = CreateSut(handler);

		// Act
		var member = await sut.GetCurrentUserGuildMember("access", 42, 80351110224678912, CancellationToken.None);

		// Assert
		Assert.That(member, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(member!.Nickname, Is.Null);
			Assert.That(member.AvatarUri, Is.Null);
		});
	}

	[Test]
	public async Task GetCurrentUserGuildMember_NotAMember_ReturnsNull()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, MemberUrl)
			.Respond(HttpStatusCode.NotFound, "application/json", """{"message":"Unknown Guild","code":10004}""");
		var sut = CreateSut(handler);

		// Act
		var member = await sut.GetCurrentUserGuildMember("access", 42, 80351110224678912, CancellationToken.None);

		// Assert
		Assert.That(member, Is.Null);
	}

	private static DiscordApiClient CreateSut(MockHttpMessageHandler handler)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		MockHttpClientFactory.Factory(mocker, AppConstants.DiscordHttpClient, handler, DiscordApiClient.BaseUri);
		return mocker.CreateInstance<DiscordApiClient>(enablePrivate: true);
	}
}
