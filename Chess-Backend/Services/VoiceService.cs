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

        var openAiApiKey = configuration["OPENAI_API_KEY"] ??
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
            var voiceId = "21m00Tcm4TlvDq8ikWAM"; // Rachel voice
            var url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

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

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("xi-api-key", _elevenLabsApiKey);

            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var audioBytes = await response.Content.ReadAsByteArrayAsync();
                _logger.LogInformation("Generated TTS audio for text: {Text} ({AudioSize} bytes)",
                    text.Substring(0, Math.Min(50, text.Length)), audioBytes.Length);
                return audioBytes;
            }
            else
            {
                _logger.LogError("ElevenLabs TTS failed with status: {StatusCode}", response.StatusCode);
                return Array.Empty<byte>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating TTS for text: {Text}", text);
            return Array.Empty<byte>();
        }
    }

    public async Task<string> SpeechToTextAsync(byte[] audioData)
    {
        try
        {
            // Create a temporary stream from the audio data
            using var audioStream = new MemoryStream(audioData);

            // Use OpenAI Whisper for speech-to-text
            var transcription = await _audioClient.TranscribeAudioAsync(audioStream, "audio.wav");
            var text = transcription.Value.Text;

            _logger.LogInformation("Transcribed audio ({AudioSize} bytes) to text: {Text}",
                audioData.Length, text);
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transcribing audio ({AudioSize} bytes)", audioData.Length);
            return "Sorry, I couldn't understand the audio.";
        }
    }
}