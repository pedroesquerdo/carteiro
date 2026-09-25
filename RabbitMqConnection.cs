using RabbitMQ.Client;

public sealed class RabbitMqConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            var settings = RabbitMqSettings.FromEnvironment();
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.User,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                AutomaticRecoveryEnabled = true,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                ClientProvidedName = "carteiro-api"
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connectionLock.Dispose();
    }
}

public sealed record RabbitMqSettings(
    string Host,
    int Port,
    string User,
    string Password,
    string VirtualHost,
    string Queue)
{
    public static RabbitMqSettings FromEnvironment()
    {
        var portText = Environment.GetEnvironmentVariable("CARTEIRO_RABBITMQ_PORT");
        var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 5672;

        return new RabbitMqSettings(
            Environment.GetEnvironmentVariable("CARTEIRO_RABBITMQ_HOST") ?? "localhost",
            port,
            Environment.GetEnvironmentVariable("CARTEIRO_RABBITMQ_USER") ?? "carteiro",
            Environment.GetEnvironmentVariable("CARTEIRO_RABBITMQ_PASSWORD") ?? "carteiro",
            Environment.GetEnvironmentVariable("CARTEIRO_RABBITMQ_VHOST") ?? "/",
            Environment.GetEnvironmentVariable("CARTEIRO_RABBITMQ_QUEUE") ?? "carteiro.emails");
    }
}
