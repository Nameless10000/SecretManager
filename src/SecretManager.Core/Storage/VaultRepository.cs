using System.Text.Json;
using Microsoft.Data.Sqlite;
using SecretManager.Core.Security;

namespace SecretManager.Core.Storage;

public sealed class VaultRepository(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=DELETE;
            PRAGMA secure_delete=ON;
            CREATE TABLE IF NOT EXISTS vault (id INTEGER PRIMARY KEY CHECK(id=1), envelope TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    public EncryptedEnvelope? Read()
    {
        if (!File.Exists(Path)) return null;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM vault WHERE id=1";
        var json = command.ExecuteScalar() as string;
        return json is null ? null : VaultCrypto.Parse(json);
    }

    public void Write(EncryptedEnvelope envelope)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO vault(id,envelope) VALUES(1,$value) ON CONFLICT(id) DO UPDATE SET envelope=excluded.envelope";
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(envelope));
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
