using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public sealed class EmailWorker(
    EmailQueue queue,
    EmailRepository repository,
    ILogger<EmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var emailId in queue.ReadAllAsync(stoppingToken))
        {
            await ProcessEmailAsync(emailId, stoppingToken);
        }
    }

    private async Task ProcessEmailAsync(long emailId, CancellationToken cancellationToken)
    {
        var email = await repository.GetAsync(emailId);
        if (email is null)
        {
            logger.LogWarning("E-mail {EmailId} não foi encontrado.", emailId);
            return;
        }

        await repository.UpdateStatusAsync(emailId, "processing");

        try
        {
            var settings = ReadSmtpSettings();
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(settings.From));
            message.To.Add(MailboxAddress.Parse(email.To));
            message.Subject = email.Subject;
            message.Body = new TextPart("plain") { Text = email.Body };

            using var client = new SmtpClient();
            await client.ConnectAsync(
                settings.Host,
                settings.Port,
                SecureSocketOptions.StartTls,
                cancellationToken);
            await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            await repository.UpdateStatusAsync(emailId, "sent", sentAt: DateTimeOffset.UtcNow);
            logger.LogInformation("E-mail {EmailId} enviado para {Recipient}.", emailId, email.To);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await repository.UpdateStatusAsync(emailId, "queued");
        }
        catch (Exception exception)
        {
            await repository.UpdateStatusAsync(emailId, "failed", exception.Message);
            logger.LogError(exception, "Falha ao enviar o e-mail {EmailId}.", emailId);
        }
    }

    private static SmtpSettings ReadSmtpSettings()
    {
        var host = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_HOST");
        var portText = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PORT");
        var user = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_USER");
        var password = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PASSWORD");
        var from = Environment.GetEnvironmentVariable("CARTEIRO_FROM");

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(from) ||
            !int.TryParse(portText, out var port))
        {
            throw new InvalidOperationException(
                "Configuração SMTP incompleta. Verifique as variáveis CARTEIRO_SMTP_* e CARTEIRO_FROM.");
        }

        return new SmtpSettings(host, port, user, password, from);
    }

    private sealed record SmtpSettings(
        string Host,
        int Port,
        string User,
        string Password,
        string From);
}
