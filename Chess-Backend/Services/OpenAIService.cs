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

    public async Task<string> GetChessResponseAsync(string userMessage, string gameId, List<Models.ChatMessage> conversationHistory, ChessPositionContext? context = null, string? customSystemPrompt = null)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Build messages list: system prompt → conversation history → current message with context
            // Use custom system prompt if provided, otherwise use default
            string systemPrompt = customSystemPrompt ?? AIPrompts.ChessOpponentSystemPrompt;
            List<OpenAI.Chat.ChatMessage> messages = new List<OpenAI.Chat.ChatMessage>
            {
                new SystemChatMessage(systemPrompt)
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

            // Format user message with position context
            string formattedMessage = FormatMessageWithContext(userMessage, context);

            // Add current user message with context
            messages.Add(new UserChatMessage(formattedMessage));

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 45
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

    public static string FormatMessageWithContext(string userMessage, ChessPositionContext? context)
    {
        if (context == null)
        {
            return userMessage;
        }

        var contextParts = new List<string>();

        // Player color and move info
        contextParts.Add($"You: {context.PlayerColor}");
        contextParts.Add($"Move {context.MoveNumber} ({context.TurnToMove} to move)");

        // Material difference
        int materialDiff = context.PlayerMaterialCentipawns - context.OpponentMaterialCentipawns;
        string materialSign = materialDiff > 0 ? "+" : "";
        contextParts.Add($"Material: {materialSign}{materialDiff}cp");

        // Piece counts
        string playerPieces = string.Join("+", context.PlayerPieces);
        string opponentPieces = string.Join("+", context.OpponentPieces);
        contextParts.Add($"(You: {playerPieces}, Opp: {opponentPieces})");

        // Recent moves
        if (context.RecentMoves.Length > 0)
        {
            contextParts.Add($"Last moves: {string.Join(", ", context.RecentMoves)}");
        }

        // Game phase
        contextParts.Add($"Phase: {context.GamePhase}");

        // Check status (only if in check)
        if (context.IsInCheck)
        {
            contextParts.Add("In check!");
        }

        // Legal move count (only if cramped)
        if (context.LegalMoveCount.HasValue)
        {
            contextParts.Add($"Only {context.LegalMoveCount} legal moves");
        }

        string contextString = "[Position from the perspective of YOU, the AI] " + string.Join(", ", contextParts);
        return $"{contextString}\nUser message: {userMessage}";
    }
}