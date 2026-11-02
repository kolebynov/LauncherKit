﻿namespace LauncherKit.Core.Models;

public readonly struct Sha256Hash : IEquatable<Sha256Hash>
{
	private readonly byte[] _hash;

	public Sha256Hash(byte[] hash)
	{
		if (hash.Length != 32)
		{
			throw new ArgumentException("Hash must be 32 bytes long", nameof(hash));
		}

		_hash = hash;
	}

	public Sha256Hash(string hash)
		: this(Convert.FromHexString(hash))
	{
	}

	public override string ToString() => Convert.ToHexString(_hash);

	public bool Equals(Sha256Hash other) => _hash.AsSpan().SequenceEqual(other._hash);

	public override bool Equals(object? obj) => obj is Sha256Hash other && Equals(other);

	public override int GetHashCode() => _hash.Aggregate(0, HashCode.Combine);

	public static bool operator ==(Sha256Hash left, Sha256Hash right) => left.Equals(right);

	public static bool operator !=(Sha256Hash left, Sha256Hash right) => !left.Equals(right);
}