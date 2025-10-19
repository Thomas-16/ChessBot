using System.ClientModel;
using OpenAI.Audio;
using System.Text;
using System.Text.Json;

namespace Chess_Backend.Services;

public class VoiceService
{
    private readonly HttpClient _httpClient;
    private readonly AudioClient _audioClient;
    private readonly ILogger<VoiceService> _logger;
    private readonly string _elevenLabsApiKey;

    public VoiceService(HttpClient httpClient, IConfiguration configuration, ILogger<VoiceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        string openAiApiKey = configuration["OPENAI_API_KEY"] ??
                              Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                              throw new InvalidOperationException("OpenAI API key not found.");

        _elevenLabsApiKey = configuration["ELEVENLABS_API_KEY"] ??
                           Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") ??
                           throw new InvalidOperationException("ElevenLabs API key not found.");

        _audioClient = new AudioClient("whisper-1", openAiApiKey);
    }

    public async Task<byte[]> TextToSpeechAsync(string text)
    {
        try
        {
            // ElevenLabs text-to-speech API
            string voiceId = "wyWA56cQNU2KqUW4eCsI"; // Some old british guy voice
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

            var payload = new
            {
                text = text,
                model_id = "eleven_monolingual_v1",
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.5
                }
            };

            StringContent content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            // Create request with headers
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            request.Headers.Add("xi-api-key", _elevenLabsApiKey);

            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                byte[] audioBytes = await response.Content.ReadAsByteArrayAsync();
                _logger.LogInformation("Generated TTS audio for text: {Text} ({AudioSize} bytes)",
                    text.Substring(0, Math.Min(50, text.Length)), audioBytes.Length);
                return audioBytes;
            }
            else
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("ElevenLabs TTS failed with status: {StatusCode}, response: {ErrorBody}",
                    response.StatusCode, errorBody);
                return Array.Empty<byte>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating TTS for text: {Text}", text);
            return Array.Empty<byte>();
        }
    }
}