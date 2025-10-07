using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chess_Backend.Config;
using Chess_Backend.Models;

namespace Chess_Backend.Services;

public class GroqService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqService> _logger;
    private readonly string _apiKey;

    public GroqService(HttpClient httpClient, IConfiguration configuration, ILogger<GroqService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _apiKey = configuration["GROQ_API_KEY"] ??
                  Environment.GetEnvironmentVariable("GROQ_API_KEY") ??
                  throw new InvalidOperationException("Groq API key not found. Set GROQ_API_KEY environment variable.");
    }

    public async Task<string> GetChessResponseAsync(string userMessage, string gameId, List<ChatMessage> conversationHistory)
    {
        try
        {
            // Build messages array: system prompt → conversation history → current message
            var messages = new List<object>
            {
                new { role = "system", content = AIPrompts.ChessOpponentSystemPrompt }
            };

            // Add conversation history
            foreach (var msg in conversationHistory)
            {
                string role = msg.Speaker == "User" ? "user" : "assistant";
                messages.Add(new { role, content = msg.Message });
            }

            // Add current user message
            messages.Add(new { role = "user", content = userMessage });

            var requestBody = new
            {
                model = "meta-llama/llama-4-scout-17b-16e-instruct",
                messages,
                temperature = 0.7,
                max_tokens = 50
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var groqResponse = JsonSerializer.Deserialize<GroqResponse>(responseContent);
                var aiMessage = groqResponse?.Choices?.FirstOrDefault()?.Message?.Content
                    ?? "I'm having trouble thinking right now. Let's focus on the game!";

                _logger.LogInformation("Generated Groq response for game {GameId}: {Response}", gameId, aiMessage);
                return aiMessage;
            }
            else
            {
                _logger.LogError("Groq API failed with status {StatusCode}: {Response}",
                    response.StatusCode, responseContent);
                return "I'm having trouble thinking right now. Let's focus on the game!";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Groq response for game {GameId}", gameId);
            return "I'm having trouble thinking right now. Let's focus on the game!";
        }
    }

    // Response models for JSON deserialization
    private class GroqResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
