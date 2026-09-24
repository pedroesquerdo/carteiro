var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new EmailRepository("Data Source=carteiro.db"));
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddHostedService<EmailWorker>();

var app = builder.Build();
var repository = app.Services.GetRequiredService<EmailRepository>();
var queue = app.Services.GetRequiredService<EmailQueue>();

await repository.InitializeAsync();
foreach (var emailId in await repository.GetRecoverableIdsAsync())
{
    await queue.EnqueueAsync(emailId);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", () => Results.Ok(new
{
    application = "Carteiro",
    stage = 4,
    description = "API HTTP com persistência e processamento assíncrono de e-mails"
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
    await queue.EnqueueAsync(emailId);

    return Results.Accepted($"/emails/{emailId}", new
    {
        id = emailId,
        status = "queued",
        to = request.To,
        message = "E-mail registrado e colocado na fila para envio."
    });
});

app.Run();

public sealed record SendEmailRequest(string To, string Subject, string Body);
