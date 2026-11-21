using System.Collections.Concurrent;
using OpenAI.Chat;

namespace LineGPT;

public sealed class ConversationMemory
{
    public const int WindowSize = 10;
    private readonly ConcurrentDictionary<string, ChatMessage[]> conversations = new(StringComparer.Ordinal);

    public ChatMessage[] GetHistory(string key) =>
        conversations.TryGetValue(key, out var messages) ? messages.ToArray() : [];

    public void AppendTurn(string key, string userMessage, string answer) =>
        conversations.AddOrUpdate(key,
            _ => [new UserChatMessage(userMessage), new AssistantChatMessage(answer)],
            (_, messages) => messages.Concat<ChatMessage>(
                [new UserChatMessage(userMessage), new AssistantChatMessage(answer)])
                .TakeLast(WindowSize).ToArray());
}
