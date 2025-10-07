using System.ClientModel;
using OpenAI.Chat;
using System.Collections.Generic;
using Chess_Backend.Config;
using Chess_Backend.Models;

namespace Chess_Backend.Services;

public class OpenAIService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIService> _logger;

    public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
    {
        string apiKey = configuration["OPENAI_API_KEY"] ??
                        Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                        throw new InvalidOperationException("OpenAI API key not found. Set OPENAI_API_KEY environment variable.");

        _chatClient = new ChatClient("gpt-4o-mini", apiKey);
        _logger = logger;
    }

    public async Task<string> GetChessResponseAsync(string userMessage, string gameId, List<Models.ChatMessage> conversationHistory)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Build messages list: system prompt → conversation history → current message
            List<OpenAI.Chat.ChatMessage> messages = new List<OpenAI.Chat.ChatMessage>
            {
                new SystemChatMessage(AIPrompts.ChessOpponentSystemPrompt)
            };

            // Add conversation history
            foreach (var msg in conversationHistory)
            {
                if (msg.Speaker == "User")
                {
                    messages.Add(new UserChatMessage(msg.Message));
                }
                else
                {
                    messages.Add(new AssistantChatMessage(msg.Message));
                }
            }

            // Add current user message
            messages.Add(new UserChatMessage(userMessage));

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 50
            };

            ClientResult<ChatCompletion>? completion = await _chatClient.CompleteChatAsync(messages, options);
            string response = completion.Value.Content[0].Text ?? "I'm having trouble thinking right now. Let's focus on the game!";

            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("⏱️ OpenAI API call took {ElapsedMs}ms for game {GameId}", elapsedMs, gameId);
            _logger.LogInformation("Generated AI response for game {GameId}: {Response}", gameId, response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI response for game {GameId}", gameId);
            return "I'm having trouble thinking right now. Let's focus on the game!";
        }
    }
}