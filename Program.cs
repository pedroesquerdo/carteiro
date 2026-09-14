using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

var smtpHost = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_HOST");
var smtpPortText = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PORT");
var smtpUser = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_USER");
var smtpPassword = Environment.GetEnvironmentVariable("CARTEIRO_SMTP_PASSWORD");
var recipient = Environment.GetEnvironmentVariable("CARTEIRO_TO");

if (string.IsNullOrWhiteSpace(smtpHost) ||
    string.IsNullOrWhiteSpace(smtpPortText) ||
    string.IsNullOrWhiteSpace(smtpUser) ||
    string.IsNullOrWhiteSpace(smtpPassword) ||
    string.IsNullOrWhiteSpace(recipient))
{
    Console.WriteLine("Configuração incompleta.");
    Console.WriteLine("Defina CARTEIRO_SMTP_HOST, CARTEIRO_SMTP_PORT, CARTEIRO_SMTP_USER, CARTEIRO_SMTP_PASSWORD e CARTEIRO_TO.");
    return;
}

if (!int.TryParse(smtpPortText, out var smtpPort))
{
    Console.WriteLine("CARTEIRO_SMTP_PORT precisa ser um número inteiro.");
    return;
}

var message = new MimeMessage();
message.From.Add(MailboxAddress.Parse(smtpUser));
message.To.Add(MailboxAddress.Parse(recipient));
message.Subject = "Primeira entrega do Carteiro";
message.Body = new TextPart("plain")
{
    Text = "Olá!\n\nEste e-mail foi enviado pela primeira versão do Carteiro.\n\n— Carteiro"
};

Console.WriteLine($"Preparando e-mail para {recipient}...");

try
{
    using var client = new SmtpClient();

    Console.WriteLine($"Conectando ao servidor SMTP {smtpHost}:{smtpPort}...");
    await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);

    Console.WriteLine("Autenticando no servidor SMTP...");
    await client.AuthenticateAsync(smtpUser, smtpPassword);

    Console.WriteLine("Enviando mensagem...");
    await client.SendAsync(message);

    await client.DisconnectAsync(true);

    Console.WriteLine("E-mail entregue ao servidor SMTP com sucesso.");
}
catch (Exception ex)
{
    Console.WriteLine("Falha ao enviar o e-mail.");
    Console.WriteLine(ex.Message);
}
