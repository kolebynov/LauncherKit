using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Services;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class PluginListMergerTests
{
	[Test]
	public void MergePlugins_NewPlugins_InsertsThemAfterPrecedingMergedPluginOrBeforeFirstListedOne()
	{
		// Arrange
		string[] current = ["# header", "*A.esp", "*B.esp", "C.esp", "*D.esp"];
		string[] merged = ["*First.esp", "*B.esp", "*AfterB.esp", "AfterB2.esp", "*D.esp"];

		// Act
		var result = PluginListMerger.MergePlugins(current, merged);

		// Assert
		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.Lines, Is.EqualTo(new[]
			{
				"# header", "*A.esp", "*First.esp", "*B.esp", "*AfterB.esp", "AfterB2.esp", "C.esp", "*D.esp",
			}));
			Assert.That((result.AddedCount, result.MovedCount, result.ChangedStateCount), Is.EqualTo((3, 0, 0)));
		}
	}

	[Test]
	public void MergePlugins_ListedPluginOutOfOrder_MovesItLaterAndAppliesMergedStateKeepingCurrentCase()
	{
		// Arrange
		string[] current = ["*A.esp", "*B.esp", "*C.esp", "*D.esp", "E.esp"];
		string[] merged = ["c.esp", "*A.esp", "*E.esp"];

		// Act
		var result = PluginListMerger.MergePlugins(current, merged);

		// Assert
		using (Assert.EnterMultipleScope())
		{
			// A or C has to move; A moves after C rather than C before A.
			Assert.That(result.Lines, Is.EqualTo(new[] { "*B.esp", "C.esp", "*A.esp", "*D.esp", "*E.esp" }));
			Assert.That((result.AddedCount, result.MovedCount, result.ChangedStateCount), Is.EqualTo((0, 1, 2)));
		}
	}

	[Test]
	public void MergePlugins_ListedPluginsAfterPreceding_MovesThemRightAfterIt()
	{
		// Arrange
		string[] current = ["*Patch.esp", "*A.esp", "*Base.esp", "*B.esp"];
		string[] merged = ["*Base.esp", "*Patch.esp", "*B.esp"];

		// Act
		var result = PluginListMerger.MergePlugins(current, merged);

		// Assert
		Assert.That(result.Lines, Is.EqualTo(new[] { "*A.esp", "*Base.esp", "*Patch.esp", "*B.esp" }));
	}

	[Test]
	public void MergePlugins_NoMergedPluginListed_AppendsThemInOrder()
	{
		// Arrange
		string[] current = ["# header", "*A.esp", ""];
		string[] merged = ["# comment", "*X.esp", "", "Y.esp"];

		// Act
		var result = PluginListMerger.MergePlugins(current, merged);

		// Assert
		Assert.That(result.Lines, Is.EqualTo(new[] { "# header", "*A.esp", "*X.esp", "Y.esp" }));
	}

	[Test]
	public void MergePlugins_PluginListedTwiceInMergedFile_Throws()
	{
		// Arrange
		string[] current = ["*A.esp"];
		string[] merged = ["*X.esp", "x.esp"];

		// Act
		// Assert
		Assert.That(() => PluginListMerger.MergePlugins(current, merged),
			Throws.TypeOf<DuplicatePluginException>().With.Property(nameof(DuplicatePluginException.PluginName)).EqualTo("X.esp"));
	}

	[Test]
	public void BuildLoadOrder_PluginsTxtReplaced_KeepsPrimaryPluginsFirstAndDropsRemovedPlugins()
	{
		// Arrange
		string[] loadOrder = ["# header", "Skyrim.esm", "Update.esm", "B.esp", "Removed.esp", "A.esp"];
		string[] previousPlugins = ["*A.esp", "*B.esp", "Removed.esp"];
		string[] plugins = ["# plugins header", "*a.esp", "New.esp", "*B.esp"];

		// Act
		var result = PluginListMerger.BuildLoadOrder(loadOrder, previousPlugins, plugins);

		// Assert
		Assert.That(result, Is.EqualTo(new[] { "# header", "Skyrim.esm", "Update.esm", "a.esp", "New.esp", "B.esp" }));
	}

	[Test]
	public void RevertMerge_PluginsChangedSinceMerge_RestoresMergedPluginsAndKeepsOtherChanges()
	{
		// Arrange
		string[] previous = ["*Base.esp", "*Patch.esp", "Old.esp", "*User.esp"];
		string[] merged = ["*Base.esp", "*New.esp", "Patch.esp"];
		// Since the merge, NewUser.esp was added after New.esp, Old.esp activated, and User.esp removed.
		string[] current = ["# header", "*Base.esp", "*New.esp", "*NewUser.esp", "Patch.esp", "*Old.esp"];

		// Act
		var result = PluginListMerger.RevertMerge(current, merged, previous);

		// Assert
		Assert.That(result, Is.EqualTo(new[] { "# header", "*Base.esp", "*Patch.esp", "*NewUser.esp", "*Old.esp" }));
	}

	[TestCase(new[] { "*X.esp", "*M.esp", "*Y.esp" }, new[] { "M.esp" }, new[] { "*Y.esp", "*X.esp", "M.esp" },
		new[] { "*Y.esp", "*X.esp", "*M.esp" })]
	// The plugins that preceded M.esp before the merge still do, though they were swapped since.
	[TestCase(new[] { "*P.esp", "*Q.esp", "*M.esp" }, new[] { "M.esp" }, new[] { "*Q.esp", "*P.esp", "M.esp" },
		new[] { "*Q.esp", "*P.esp", "*M.esp" })]
	[TestCase(new[] { "*A.esp", "*B.esp", "*C.esp", "*D.esp" }, new[] { "*A.esp", "*C.esp" },
		new[] { "*A.esp", "*B.esp", "*D.esp", "*C.esp", "*X.esp" }, new[] { "*A.esp", "*B.esp", "*C.esp", "*D.esp", "*X.esp" })]
	// X.esp was placed after A.esp, which the merge had moved and the rollback moves back before B.esp.
	[TestCase(new[] { "*A.esp", "*B.esp", "*C.esp", "*D.esp" }, new[] { "*C.esp", "*A.esp" },
		new[] { "*B.esp", "*C.esp", "*A.esp", "*X.esp", "*D.esp" }, new[] { "*A.esp", "*B.esp", "*C.esp", "*X.esp", "*D.esp" })]
	public void RevertMerge_OtherPluginsAddedOrReorderedSinceMerge_KeepsTheirCurrentOrder(string[] previous, string[] merged,
		string[] current, string[] expected)
	{
		// Act
		var result = PluginListMerger.RevertMerge(current, merged, previous);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[TestCase(new[] { "*A.esp", "*B.esp", "C.esp", "*D.esp" }, new[] { "C.esp", "*New.esp", "*A.esp" })]
	[TestCase(new[] { "*A.esp", "*B.esp", "*C.esp", "*D.esp", "E.esp" }, new[] { "*B.esp", "*D.esp", "*A.esp", "E.esp" })]
	[TestCase(new[] { "*A.esp" }, new[] { "*X.esp", "Y.esp" })]
	public void RevertMerge_NothingChangedSinceMerge_ReturnsPreviousList(string[] previous, string[] merged)
	{
		// Arrange
		var current = PluginListMerger.MergePlugins(previous, merged).Lines;

		// Act
		var result = PluginListMerger.RevertMerge(current, merged, previous);

		// Assert
		Assert.That(result, Is.EqualTo(previous));
	}
}
