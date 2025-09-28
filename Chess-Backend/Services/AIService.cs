using OpenAI.Chat;
using System.Collections.Generic;

namespace Chess_Backend.Services;

public class AIService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AIService> _logger;

    public AIService(IConfiguration configuration, ILogger<AIService> logger)
    {
        var apiKey = configuration["OPENAI_API_KEY"] ??
                    Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                    throw new InvalidOperationException("OpenAI API key not found. Set OPENAI_API_KEY environment variable.");

        _chatClient = new ChatClient("gpt-4o-mini", apiKey);
        _logger = logger;
    }

    public async Task<string> GetChessResponseAsync(string userMessage, string gameId)
    {
        try
        {
            var systemPrompt = "You are a witty chess opponent. Keep responses brief and chess-focused. " +
                              "Be encouraging but competitive. Make occasional chess puns and references to chess strategy. " +
                              "Keep responses under 100 words.";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage($"Game {gameId}: {userMessage}")
            };

            var completion = await _chatClient.CompleteChatAsync(messages);
            var response = completion.Value.Content[0].Text ?? "I'm having trouble thinking right now. Let's focus on the game!";

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