using Microsoft.Data.Sqlite;

public sealed class EmailRepository(string connectionString)
{
    public async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                recipient TEXT NOT NULL,
                subject TEXT NOT NULL,
                body TEXT NOT NULL,
                status TEXT NOT NULL,
                error_message TEXT NULL,
                created_at TEXT NOT NULL,
                sent_at TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_emails_status ON emails(status);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<EmailRecord>> ListAsync()
    {
        var emails = new List<EmailRecord>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recipient, subject, body, status, error_message, created_at, sent_at
            FROM emails
            ORDER BY id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            emails.Add(ReadEmail(reader));
        }

        return emails;
    }

    public async Task<EmailRecord?> GetAsync(long id)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recipient, subject, body, status, error_message, created_at, sent_at
            FROM emails
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadEmail(reader) : null;
    }

    public async Task<long> CreateAsync(SendEmailRequest request)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO emails (recipient, subject, body, status, created_at)
            VALUES ($recipient, $subject, $body, 'queued', $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$recipient", request.To);
        command.Parameters.AddWithValue("$subject", request.Subject);
        command.Parameters.AddWithValue("$body", request.Body);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));

        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task<IReadOnlyList<long>> GetRecoverableIdsAsync()
    {
        var ids = new List<long>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM emails
            WHERE status IN ('queued', 'processing')
            ORDER BY id;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    public async Task UpdateStatusAsync(
        long id,
        string status,
        string? errorMessage = null,
        DateTimeOffset? sentAt = null)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE emails
            SET status = $status, error_message = $errorMessage, sent_at = $sentAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$sentAt", sentAt is null ? DBNull.Value : sentAt.Value.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static EmailRecord ReadEmail(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        DateTimeOffset.Parse(reader.GetString(6)),
        reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));
}

public sealed record EmailRecord(
    long Id,
    string To,
    string Subject,
    string Body,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);
