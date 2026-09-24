using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Data.Sqlite;
using MimeKit;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var connectionString = "Data Source=carteiro.db";

await InitializeDatabaseAsync(connectionString);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", () => Results.Ok(new
{
    application = "Carteiro",
    stage = 3,
    description = "API HTTP com persistência de e-mails e envio síncrono via SMTP"
}));

app.MapGet("/emails", async () =>
{
    var emails = new List<EmailRecord>();

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

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

    return Results.Ok(emails);
});

app.MapGet("/emails/{id:long}", async (long id) =>
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = """
        SELECT id, recipient, subject, body, status, error_message, created_at, sent_at
        FROM emails
        WHERE id = $id;
        """;
    command.Parameters.AddWithValue("$id", id);

    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? Results.Ok(ReadEmail(reader))
        : Results.NotFound(new { error = "E-mail não encontrado." });
});

app.MapPost("/emails", async (SendEmailRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.To) ||
        string.IsNullOrWhiteSpace(request.Subject) ||
        string.IsNullOrWhiteSpace(request.Body))
    {
        return Results.BadRequest(new
        {
            error = "Os campos to, subject e body são obrigatórios."
        });
    }

    var smtpHost = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_HOST");
    var smtpPortText = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PORT");
    var smtpUser = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_USER");
    var smtpPassword = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PASSWORD");
    var from = Environment.GetEnvironmentVariable("CARTEIRO_FROM");

    if (string.IsNullOrWhiteSpace(smtpHost) ||
        string.IsNullOrWhiteSpace(smtpPortText) ||
        string.IsNullOrWhiteSpace(smtpUser) ||
        string.IsNullOrWhiteSpace(smtpPassword) ||
        string.IsNullOrWhiteSpace(from))
    {
        return Results.Problem(
            "Configuração SMTP incompleta. Defina CARTEIRO_SMTP_HOST, CARTEIRO_SMTP_PORT, CARTEIRO_SMTP_USER, CARTEIRO_SMTP_PASSWORD e CARTEIRO_FROM.");
    }

    if (!int.TryParse(smtpPortText, out var smtpPort))
    {
        return Results.Problem("CARTEIRO_SMTP_PORT precisa ser um número inteiro.");
    }

    var emailId = await CreateEmailAsync(connectionString, request);
    var message = new MimeMessage();
    message.From.Add(MailboxAddress.Parse(from));
    message.To.Add(MailboxAddress.Parse(request.To));
    message.Subject = request.Subject;
    message.Body = new TextPart("plain") { Text = request.Body };

    try
    {
        using var client = new SmtpClient();

        await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(smtpUser, smtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        await UpdateEmailStatusAsync(connectionString, emailId, "sent", null, DateTimeOffset.UtcNow);

        return Results.Ok(new
        {
            id = emailId,
            status = "sent",
            from,
            to = request.To,
            message = "E-mail entregue ao servidor SMTP com sucesso."
        });
    }
    catch (Exception ex)
    {
        await UpdateEmailStatusAsync(connectionString, emailId, "failed", ex.Message, null);

        return Results.Problem(
            detail: ex.Message,
            title: "Falha ao enviar o e-mail",
            statusCode: StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?> { ["emailId"] = emailId });
    }
});

app.Run();

static async Task InitializeDatabaseAsync(string connectionString)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = """
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
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task<long> CreateEmailAsync(string connectionString, SendEmailRequest request)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO emails (recipient, subject, body, status, created_at)
        VALUES ($recipient, $subject, $body, 'pending', $createdAt);
        SELECT last_insert_rowid();
        """;
    command.Parameters.AddWithValue("$recipient", request.To);
    command.Parameters.AddWithValue("$subject", request.Subject);
    command.Parameters.AddWithValue("$body", request.Body);
    command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));

    return (long)(await command.ExecuteScalarAsync())!;
}

static async Task UpdateEmailStatusAsync(
    string connectionString,
    long id,
    string status,
    string? errorMessage,
    DateTimeOffset? sentAt)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

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

static EmailRecord ReadEmail(SqliteDataReader reader) => new(
    reader.GetInt64(0),
    reader.GetString(1),
    reader.GetString(2),
    reader.GetString(3),
    reader.GetString(4),
    reader.IsDBNull(5) ? null : reader.GetString(5),
    DateTimeOffset.Parse(reader.GetString(6)),
    reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));

record SendEmailRequest(string To, string Subject, string Body);

record EmailRecord(
    long Id,
    string To,
    string Subject,
    string Body,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);
