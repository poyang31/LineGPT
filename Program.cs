using LineGPT;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpClient<WebSearchService>();
builder.Services.AddSingleton<AgentService>();
builder.Services.AddSingleton<LineWebhook>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<LineWebhook>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/callback", (HttpRequest request, LineWebhook webhook, CancellationToken cancellationToken) =>
    webhook.HandleAsync(request, cancellationToken))
    .WithName("LineWebhook");

app.Run();
