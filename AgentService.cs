using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace LineGPT;

public sealed class AgentService(IConfiguration configuration)
{
    public async Task<string> RunAsync(string message, CancellationToken cancellationToken)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configure OpenAI:ApiKey before calling the agent.");

        var baseUrl = configuration["OpenAI:BaseUrl"];
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("OpenAI:BaseUrl must be an absolute HTTP or HTTPS URL.");

            options.Endpoint = endpoint;
        }

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options)
            .GetChatClient(configuration["OpenAI:Model"] ?? "gpt-6-luna");

        ChatMessage[] messages =
        [
            new SystemChatMessage("You are a helpful assistant. Respond in the user's language."),
            new UserChatMessage(message)
        ];
        var response = await client.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        return string.Concat(response.Value.Content.Select(part => part.Text));
    }
}
