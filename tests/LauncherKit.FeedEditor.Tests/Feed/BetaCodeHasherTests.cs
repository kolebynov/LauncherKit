using LauncherKit.Core.Feed;
using LauncherKit.FeedEditor.Feed;

namespace LauncherKit.FeedEditor.Tests.Feed;

[TestFixture]
public sealed class BetaCodeHasherTests
{
	[TestCase("early-access")]
	[TestCase("Бета-Code")]
	[TestCase("")]
	public void Hash_BetaCode_MatchesLauncherHash(string betaCode)
	{
		// Act
		var result = BetaCodeHasher.Hash(betaCode);

		// Assert
		Assert.That(result, Is.EqualTo(FeedProvider.HashBetaCode(betaCode)));
	}
}
