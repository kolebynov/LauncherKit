using System.Diagnostics.CodeAnalysis;

namespace LauncherKit.Core.Choices;

/// <summary>
/// A selectable option of a <see cref="ChoiceStep"/>. Compared by reference, so an option identifies itself
/// in the selection result even when several options share a name.
/// </summary>
public sealed class ChoiceOption
{
	private readonly bool _isRequired;
	private readonly ChoiceText? _unavailableReason;

	public required ChoiceText Name { get; init; }

	public required ChoiceText Description { get; init; }

	/// <summary>A required option is always selected and cannot be deselected. A required option cannot be unavailable.</summary>
	public bool IsRequired
	{
		get => _isRequired;
		init
		{
			if (value && _unavailableReason is not null)
			{
				throw new ArgumentException("A required option cannot be unavailable", nameof(IsRequired));
			}

			_isRequired = value;
		}
	}

	/// <summary>
	/// The option starts selected; unlike a required option, the user may deselect it.
	/// Ignored for an unavailable option, which is never selected.
	/// </summary>
	public bool IsSelectedByDefault { get; init; }

	/// <summary>
	/// Why the option cannot be selected, shown to the user. <see langword="null"/> for an available option.
	/// An unavailable option is never selected and does not count towards <see cref="ChoiceSelectionMode.All"/>.
	/// </summary>
	public ChoiceText? UnavailableReason
	{
		get => _unavailableReason;
		init
		{
			if (value is not null)
			{
				if (value.Value.Value is ChoiceText.Content { Value: var content })
				{
					ArgumentException.ThrowIfNullOrWhiteSpace(content, nameof(UnavailableReason));
				}
				if (_isRequired)
				{
					throw new ArgumentException("A required option cannot be unavailable", nameof(UnavailableReason));
				}
			}

			_unavailableReason = value;
		}
	}

	[MemberNotNullWhen(false, nameof(UnavailableReason))]
	public bool IsAvailable => _unavailableReason is null;
}
