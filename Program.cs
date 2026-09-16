using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    application = "Carteiro",
    stage = 2,
    description = "API HTTP com envio síncrono de e-mail via SMTP"
}));

app.MapPost("/emails", async (SendEmailRequest request) =>
{
    var smtpHost = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_HOST");
    var smtpPortText = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PORT");
    var smtpUser = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_USER");
    var smtpPassword = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PASSWORD");

    var from = Environment.GetEnvironmentVariable("CARTEIRO_FROM");

    if (string.IsNullOrWhiteSpace(smtpHost) ||
        string.IsNullOrWhiteSpace(smtpPortText) ||
        string.IsNullOrWhiteSpace(smtpUser) ||
        string.IsNullOrWhiteSpace(smtpPassword))
    {
        return Results.Problem(
            "Configuração SMTP incompleta. Defina CARTEIRO_SMTP_HOST, CARTEIRO_SMTP_PORT, CARTEIRO_SMTP_USER e CARTEIRO_SMTP_PASSWORD.");
    }

    if (!int.TryParse(smtpPortText, out var smtpPort))
    {
        return Results.Problem("CARTEIRO_SMTP_PORT precisa ser um número inteiro.");
    }

    if (string.IsNullOrWhiteSpace(request.To) ||
        string.IsNullOrWhiteSpace(request.Subject) ||
        string.IsNullOrWhiteSpace(request.Body))
    {
        return Results.BadRequest(new
        {
            error = "Os campos to, subject e body são obrigatórios."
        });
    }

    var message = new MimeMessage();
    message.From.Add(MailboxAddress.Parse(from));
    message.To.Add(MailboxAddress.Parse(request.To));
    message.Subject = request.Subject;
    message.Body = new TextPart("plain")
    {
        Text = request.Body
    };

    try
    {
        using var client = new SmtpClient();

        await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(smtpUser, smtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        return Results.Ok(new
        {
            status = "sent",
            to = request.To,
            message = "E-mail entregue ao servidor SMTP com sucesso."
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Falha ao enviar o e-mail",
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

record SendEmailRequest(string To, string Subject, string Body);
