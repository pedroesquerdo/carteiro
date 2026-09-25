using System.Text;
using RabbitMQ.Client;

public sealed class RabbitMqPublisher(RabbitMqConnection rabbitMqConnection)
{
    public async Task PublishAsync(long emailId, CancellationToken cancellationToken = default)
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

        var properties = new BasicProperties
        {
            ContentType = "text/plain",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = emailId.ToString()
        };
        var body = Encoding.UTF8.GetBytes(emailId.ToString());

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: settings.Queue,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
