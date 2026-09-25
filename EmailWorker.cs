using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public sealed class EmailWorker(
    RabbitMqConnection rabbitMqConnection,
    RabbitMqPublisher publisher,
    EmailRepository repository,
    ILogger<EmailWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RabbitMQ indisponível. Nova tentativa em 5 segundos.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var settings = RabbitMqSettings.FromEnvironment();
        var connection = await rabbitMqConnection.GetAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: settings.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: cancellationToken);

        foreach (var emailId in await repository.GetIdsByStatusAsync("processing"))
        {
            await repository.UpdateStatusAsync(emailId, "queued");
            await publisher.PublishAsync(emailId, cancellationToken);
        }

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var value = Encoding.UTF8.GetString(eventArgs.Body.Span);
            if (long.TryParse(value, out var emailId))
            {
                await ProcessEmailAsync(channel, eventArgs.DeliveryTag, emailId, cancellationToken);
            }
            else
            {
                logger.LogWarning("Mensagem inválida recebida do RabbitMQ: {Message}", value);
                await channel.BasicNackAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: cancellationToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: settings.Queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation("Consumindo a fila RabbitMQ {Queue}.", settings.Queue);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task ProcessEmailAsync(
        IChannel channel,
        ulong deliveryTag,
        long emailId,
        CancellationToken cancellationToken)
    {
        var email = await repository.GetAsync(emailId);
        if (email is null)
        {
            logger.LogWarning("E-mail {EmailId} não foi encontrado.", emailId);
            await channel.BasicNackAsync(
                deliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken: cancellationToken);
            return;
        }

        var attempt = email.AttemptCount + 1;
        await repository.StartAttemptAsync(emailId);

        try
        {
            var settings = ReadSmtpSettings();
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(settings.From));
            message.To.Add(MailboxAddress.Parse(email.To));
            message.Subject = email.Subject;
            message.Body = new TextPart("plain") { Text = email.Body };

            using var client = new SmtpClient();
            await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            await repository.UpdateStatusAsync(emailId, "sent", sentAt: DateTimeOffset.UtcNow);
            await channel.BasicAckAsync(
                deliveryTag,
                multiple: false,
                cancellationToken: cancellationToken);
            logger.LogInformation("E-mail {EmailId} enviado para {Recipient}.", emailId, email.To);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await repository.UpdateStatusAsync(emailId, "processing");
        }
        catch (Exception exception)
        {
            if (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await repository.UpdateStatusAsync(
                    emailId,
                    "queued",
                    $"Tentativa {attempt}/{MaxAttempts}: {exception.Message}");

                logger.LogWarning(
                    exception,
                    "Tentativa {Attempt}/{MaxAttempts} do e-mail {EmailId} falhou. Retry em {DelaySeconds}s.",
                    attempt,
                    MaxAttempts,
                    emailId,
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
                await channel.BasicNackAsync(
                    deliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await repository.UpdateStatusAsync(
                    emailId,
                    "failed",
                    $"Tentativa {attempt}/{MaxAttempts}: {exception.Message}");
                await channel.BasicNackAsync(
                    deliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: cancellationToken);
                logger.LogError(
                    exception,
                    "E-mail {EmailId} descartado após {Attempts} tentativas.",
                    emailId,
                    attempt);
            }
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

    private sealed record SmtpSettings(string Host, int Port, string User, string Password, string From);
}
