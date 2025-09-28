using Chess_Backend.Data;
using Chess_Backend.Models;
using Chess_Backend.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Chess_Backend.Hubs;

public class ChessHub : Hub
{
    private readonly ChessDbContext _context;
    private readonly AIService _aiService;
    private readonly VoiceService _voiceService;
    private readonly ILogger<ChessHub> _logger;

    public ChessHub(ChessDbContext context, AIService aiService, VoiceService voiceService, ILogger<ChessHub> logger)
    {
        _context = context;
        _aiService = aiService;
        _voiceService = voiceService;
        _logger = logger;
    }

    public async Task<string> StartNewGame()
    {
        try
        {
            var game = new Game();
            _context.Games.Add(game);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Started new game with ID: {GameId}", game.Id);
            return game.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting new game");
            throw new HubException("Failed to start new game");
        }
    }

    public async Task SendChatMessage(string gameId, string message)
    {
        try
        {
            // Validate game exists
            var gameExists = await _context.Games.AnyAsync(g => g.Id == gameId);
            if (!gameExists)
            {
                throw new HubException("Game not found");
            }

            // Save user message
            var userMessage = new ChatMessage
            {
                GameId = gameId,
                Speaker = "User",
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            _context.ChatMessages.Add(userMessage);

            // Get AI response
            var aiResponse = await _aiService.GetChessResponseAsync(message, gameId);

            // Save AI message
            var aiMessage = new ChatMessage
            {
                GameId = gameId,
                Speaker = "AI",
                Message = aiResponse,
                Timestamp = DateTime.UtcNow
            };
            _context.ChatMessages.Add(aiMessage);

            await _context.SaveChangesAsync();

            // Generate speech for AI response
            var audioBytes = await _voiceService.TextToSpeechAsync(aiResponse);
            var audioBase64 = Convert.ToBase64String(audioBytes);

            // Send response back to client
            await Clients.Caller.SendAsync("ReceiveChatResponse", new
            {
                gameId = gameId,
                message = aiResponse,
                audio = audioBase64,
                timestamp = aiMessage.Timestamp
            });

            _logger.LogInformation("Processed chat message for game {GameId}", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat message for game {GameId}", gameId);
            await Clients.Caller.SendAsync("ChatError", "Failed to process message");
        }
    }

    public async Task SendVoiceMessage(string gameId, string base64Audio)
    {
        try
        {
            // Validate game exists
            var gameExists = await _context.Games.AnyAsync(g => g.Id == gameId);
            if (!gameExists)
            {
                throw new HubException("Game not found");
            }

            // Convert base64 to bytes
            var audioBytes = Convert.FromBase64String(base64Audio);

            // Convert speech to text
            var transcribedText = await _voiceService.SpeechToTextAsync(audioBytes);

            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                await Clients.Caller.SendAsync("ChatError", "Could not understand the audio");
                return;
            }

            // Process as regular chat message
            await SendChatMessage(gameId, transcribedText);

            _logger.LogInformation("Processed voice message for game {GameId}: {TranscribedText}", gameId, transcribedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing voice message for game {GameId}", gameId);
            await Clients.Caller.SendAsync("ChatError", "Failed to process voice message");
        }
    }

    public async Task SaveGameResult(string gameId, string pgn, string result)
    {
        try
        {
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == gameId);
            if (game == null)
            {
                throw new HubException("Game not found");
            }

            game.PGN = pgn;
            game.Result = result;
            game.EndTime = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await Clients.Caller.SendAsync("GameSaved", gameId);

            _logger.LogInformation("Saved game result for game {GameId}: {Result}", gameId, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving game result for game {GameId}", gameId);
            await Clients.Caller.SendAsync("SaveError", "Failed to save game result");
        }
    }

    public async Task GetChatHistory(string gameId)
    {
        try
        {
            var messages = await _context.ChatMessages
                .Where(m => m.GameId == gameId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new
                {
                    speaker = m.Speaker,
                    message = m.Message,
                    timestamp = m.Timestamp
                })
                .ToListAsync();

            await Clients.Caller.SendAsync("ChatHistory", messages);

            _logger.LogInformation("Retrieved chat history for game {GameId}: {MessageCount} messages", gameId, messages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat history for game {GameId}", gameId);
            await Clients.Caller.SendAsync("ChatError", "Failed to retrieve chat history");
        }
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}