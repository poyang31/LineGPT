using Line;
using Newtonsoft.Json;
using System.Threading.Channels;

namespace LineGPT;

public sealed class LineWebhook(IConfiguration configuration, AgentService agent, ILogger<LineWebhook> logger) : BackgroundService
{
    private readonly Channel<(string Text, string ReplyToken)> pending =
        Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    // Reuse the SDK client and its underlying HttpClient across requests.
    private readonly Lazy<ILineBot> bot = new(() => new LineBot(new LineConfiguration(
        configuration["Line:ChannelAccessToken"]!, configuration["Line:ChannelSecret"]!)));

    public async Task<IResult> HandleAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["Line:ChannelSecret"]) ||
            string.IsNullOrWhiteSpace(configuration["Line:ChannelAccessToken"]))
            return Results.Problem("Configure LINE channel credentials before using the webhook.", statusCode: 503);

        if (string.IsNullOrWhiteSpace(request.Headers["X-Line-Signature"]))
            return Results.Unauthorized();

        ILineEvents events;
        try
        {
            // The SDK verifies HMAC-SHA256 over the original body before parsing JSON.
            events = await bot.Value.GetEvents(request);
        }
        catch (LineBotException)
        {
            return Results.Unauthorized();
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }

        foreach (var evt in events)
        {
            if (evt.EventType != LineEventType.Message || string.IsNullOrWhiteSpace(evt.Message?.Text) ||
                string.IsNullOrWhiteSpace(evt.ReplyToken) ||
                evt.ReplyToken is "00000000000000000000000000000000" or "ffffffffffffffffffffffffffffffff")
                continue;

            if (!pending.Writer.TryWrite((evt.Message.Text, evt.ReplyToken)))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in pending.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ReplyAsync(evt.Text, evt.ReplyToken, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("LINE reply failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }

    private async Task ReplyAsync(string message, string replyToken, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            text = await agent.RunAsync(message, timeout.Token);
            if (string.IsNullOrWhiteSpace(text))
                text = "暫時無法產生回覆，請稍後再試。";
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Avoid logging provider exceptions, which may contain message content.
            logger.LogWarning("Agent request failed ({ExceptionType}).", exception.GetType().Name);
            text = "暫時無法產生回覆，請稍後再試。";
        }

        // This SDK version enforces a 2,000 UTF-16 code unit limit.
        if (text.Length > 2000)
        {
            var length = char.IsHighSurrogate(text[1998]) ? 1998 : 1999;
            text = text[..length] + "…";
        }
        await bot.Value.Reply(replyToken, new TextMessage(text));
    }

    private sealed record LineConfiguration(string ChannelAccessToken, string ChannelSecret) : ILineConfiguration;
}
