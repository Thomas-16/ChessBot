using System.ClientModel;
using OpenAI.Chat;
using System.Collections.Generic;
using Chess_Backend.Config;

namespace Chess_Backend.Services;

public class AIService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AIService> _logger;

    public AIService(IConfiguration configuration, ILogger<AIService> logger)
    {
        string apiKey = configuration["OPENAI_API_KEY"] ??
                        Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                        throw new InvalidOperationException("OpenAI API key not found. Set OPENAI_API_KEY environment variable.");

        _chatClient = new ChatClient("gpt-4o-mini", apiKey);
        _logger = logger;
    }

    public async Task<string> GetChessResponseAsync(string userMessage, string gameId)
    {
        try
        {
            List<ChatMessage> messages = new List<ChatMessage>
            {
                new SystemChatMessage(AIPrompts.ChessOpponentSystemPrompt),
                new UserChatMessage(userMessage)
            };

            ClientResult<ChatCompletion>? completion = await _chatClient.CompleteChatAsync(messages);
            string response = completion.Value.Content[0].Text ?? "I'm having trouble thinking right now. Let's focus on the game!";

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