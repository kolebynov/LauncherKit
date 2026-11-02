using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.OAuth;

[TestFixture]
public sealed class ProtectedOAuthTokenStoreTests
{
	[Test]
	public void Get_AfterSet_ReturnsTokenThatIsNotStoredAsPlainText()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		string? stored = null;
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.Set("OAuthToken_Discord", It.IsAny<string?>())).Callback((string _, string? value) => stored = value);
		storage.Setup(x => x.Get("OAuthToken_Discord")).Returns(() => stored);
		var sut = mocker.CreateInstance<ProtectedOAuthTokenStore>(enablePrivate: true);
		var token = new OAuthToken("secret-access", "secret-refresh", new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

		// Act
		sut.Set("Discord", token);
		var restored = sut.Get("Discord");

		// Assert
		Assert.That(restored, Is.EqualTo(token));
		Assert.That(stored, Does.Not.Contain("secret"));
		Assert.That(Convert.FromBase64String(stored!).AsSpan().IndexOf("secret"u8), Is.EqualTo(-1));
	}

	[Test]
	public void SuppliedProduct_ReadsAndWritesLegacyTokenFormatUsingItsEntropy()
	{
		// Arrange: a token in the format released launchers stored, encrypted with the product's entropy.
		var legacyJson = Encoding.UTF8.GetBytes("""
			{"accessToken":"legacy-access","refreshToken":"legacy-refresh","expiresAt":"2026-11-02T12:00:00+00:00"}
			""");
		var entropy = Encoding.UTF8.GetBytes(ProductTestSupport.CreateProduct().OAuthTokenEntropy);
		var stored = Convert.ToBase64String(ProtectedData.Protect(legacyJson, entropy, DataProtectionScope.CurrentUser));
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker();
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.Get("OAuthToken_Discord")).Returns(() => stored);
		storage.Setup(x => x.Set("OAuthToken_Discord", It.IsAny<string?>()))
			.Callback((string _, string? value) => stored = value!);
		var sut = mocker.CreateInstance<ProtectedOAuthTokenStore>(enablePrivate: true);
		var expiry = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
		var replacement = new OAuthToken("new-access", "new-refresh", expiry.AddDays(1));

		// Act
		var legacy = sut.Get("Discord");
		sut.Set("Discord", replacement);

		// Assert
		Assert.That(legacy, Is.EqualTo(new OAuthToken("legacy-access", "legacy-refresh", expiry)));
		using var document = JsonDocument.Parse(ProtectedData.Unprotect(Convert.FromBase64String(stored), entropy, DataProtectionScope.CurrentUser));
		Assert.Multiple(() =>
		{
			Assert.That(document.RootElement.GetProperty("accessToken").GetString(), Is.EqualTo(replacement.AccessToken));
			Assert.That(document.RootElement.GetProperty("refreshToken").GetString(), Is.EqualTo(replacement.RefreshToken));
			Assert.That(document.RootElement.GetProperty("expiresAt").GetDateTimeOffset(), Is.EqualTo(replacement.ExpiresAt));
		});
	}

	[Test]
	public void IndependentIdentities_CannotDecryptEachOthersTokens()
	{
		var first = ProductTestSupport.CreateProduct();
		var second = ProductTestSupport.CreateProduct(oauthTokenEntropy: "OtherLauncher.OAuthToken");
		string? stored = null;
		var mocker = ProductTestSupport.CreateMocker();
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.Set("OAuthToken_Discord", It.IsAny<string?>()))
			.Callback((string _, string? value) => stored = value);
		storage.Setup(x => x.Get("OAuthToken_Discord")).Returns(() => stored);
		OptionsStubs.Options(mocker, first);
		var firstStore = mocker.CreateInstance<ProtectedOAuthTokenStore>(enablePrivate: true);
		OptionsStubs.Options(mocker, second);
		var secondStore = mocker.CreateInstance<ProtectedOAuthTokenStore>(enablePrivate: true);
		var token = new OAuthToken("access", "refresh", DateTimeOffset.UtcNow);

		firstStore.Set("Discord", token);

		Assert.That(() => secondStore.Get("Discord"), Throws.InstanceOf<CryptographicException>());
		Assert.That(firstStore.Get("Discord"), Is.EqualTo(token));
	}

	[Test]
	public void InvalidIdentity_ThrowsBeforeAccessingTokenStorage()
	{
		var mocker = ProductTestSupport.CreateMocker();
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		OptionsStubs.Options(mocker, ProductTestSupport.CreateProduct(rootDirectoryName: ""));
		Assert.That(() => mocker.CreateInstance<ProtectedOAuthTokenStore>(enablePrivate: true),
			Throws.Exception.Matches<Exception>(error => error?.GetBaseException() is ArgumentException));
		storage.VerifyNoOtherCalls();
	}
}
