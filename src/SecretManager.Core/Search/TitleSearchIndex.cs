using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SecretManager.Core.Models;

namespace SecretManager.Core.Search;

public sealed class TitleSearchIndex : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Pooling=False");
    public TitleSearchIndex(IEnumerable<SecretAssociation> associations)
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE VIRTUAL TABLE titles USING fts5(id UNINDEXED, title, tokenize='unicode61 remove_diacritics 2')";
        command.ExecuteNonQuery();
        foreach (var association in associations)
        {
            command.CommandText = "INSERT INTO titles(id,title) VALUES($id,$title)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", association.Id.ToString());
            command.Parameters.AddWithValue("$title", association.Title);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlySet<Guid> Search(string query)
    {
        var tokens = Regex.Matches(query, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)
            .Select(m => m.Value).Take(32).ToArray();
        using var command = connection.CreateCommand();
        command.CommandText = tokens.Length == 0 ? "SELECT id FROM titles" : "SELECT id FROM titles WHERE titles MATCH $query";
        if (tokens.Length > 0) command.Parameters.AddWithValue("$query", string.Join(" OR ", tokens.Select(t => $"\"{t}\"*")));
        var results = new HashSet<Guid>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(Guid.Parse(reader.GetString(0)));
        return results;
    }

    public void Dispose() => connection.Dispose();
}
