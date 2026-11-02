using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace LauncherKit.Core.Db;

/// <summary>
/// A scoped SQLite database: owns one connection, opened on first use and closed with the context.
/// A connection is not thread-safe, so concurrent users of one context must serialize their calls.
/// </summary>
public abstract class SqliteDbContext : IDisposable
{
	private readonly string _connectionString;
	private SqliteConnection? _connection;

	private protected SqliteDbContext(string dbPath)
	{
		// Without pooling the database file is closed with the context, as it was with System.Data.SQLite,
		// so the directory holding it can be moved or deleted afterwards.
		_connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_connection?.Dispose();
			_connection = null;
		}
	}

	internal SqliteTransaction BeginTransaction() => GetConnection().BeginTransaction();

	/// <summary>
	/// Creates a command with named parameters; a <see langword="null"/> value is bound as SQL <c>NULL</c>.
	/// </summary>
	[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
		Justification = "Callers pass constant SQL; values are always bound as parameters.")]
	internal SqliteCommand CreateCommand(string sql, params ReadOnlySpan<(string Name, object? Value)> parameters)
	{
		var command = GetConnection().CreateCommand();
		command.CommandText = sql;
		foreach (var (name, value) in parameters)
		{
			command.Parameters.AddWithValue(name, value ?? DBNull.Value);
		}

		return command;
	}

	/// <returns>The number of rows changed by the statement.</returns>
	internal int Execute(string sql, params ReadOnlySpan<(string Name, object? Value)> parameters)
	{
		using var command = CreateCommand(sql, parameters);
		return command.ExecuteNonQuery();
	}

	/// <summary>Runs a query and maps every row with <paramref name="readRow"/>.</summary>
	internal List<T> Read<T>(string sql, Func<SqliteDataReader, T> readRow,
		params ReadOnlySpan<(string Name, object? Value)> parameters)
	{
		using var command = CreateCommand(sql, parameters);
		using var reader = command.ExecuteReader();
		var rows = new List<T>();
		while (reader.Read())
		{
			rows.Add(readRow(reader));
		}

		return rows;
	}

	private SqliteConnection GetConnection()
	{
		if (_connection != null)
		{
			return _connection;
		}

		var connection = new SqliteConnection(_connectionString);
		try
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "PRAGMA journal_mode=PERSIST;";
			command.ExecuteNonQuery();
		}
		catch
		{
			connection.Dispose();
			throw;
		}

		return _connection = connection;
	}
}
