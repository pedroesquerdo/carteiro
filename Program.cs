var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new EmailRepository("Data Source=carteiro.db"));
builder.Services.AddSingleton<RabbitMqConnection>();
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<EmailWorker>();

var app = builder.Build();
var repository = app.Services.GetRequiredService<EmailRepository>();
var publisher = app.Services.GetRequiredService<RabbitMqPublisher>();

await repository.InitializeAsync();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", () => Results.Ok(new
{
    application = "Carteiro",
    stage = 5,
    description = "API HTTP com persistência e mensageria via RabbitMQ"
}));

app.MapGet("/emails", async () => Results.Ok(await repository.ListAsync()));

app.MapGet("/emails/{id:long}", async (long id) =>
{
    var email = await repository.GetAsync(id);
    return email is null
        ? Results.NotFound(new { error = "E-mail não encontrado." })
        : Results.Ok(email);
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

    var emailId = await repository.CreateAsync(request);

    try
    {
        await publisher.PublishAsync(emailId);
    }
    catch (Exception exception)
    {
        await repository.UpdateStatusAsync(emailId, "failed", $"RabbitMQ: {exception.Message}");
        return Results.Problem(
            detail: "A remessa foi registrada, mas não foi possível publicá-la no RabbitMQ.",
            title: "Fila indisponível",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["emailId"] = emailId });
    }

    return Results.Accepted($"/emails/{emailId}", new
    {
        id = emailId,
        status = "queued",
        to = request.To,
        message = "E-mail registrado e publicado no RabbitMQ."
    });
});

app.Run();

public sealed record SendEmailRequest(string To, string Subject, string Body);
