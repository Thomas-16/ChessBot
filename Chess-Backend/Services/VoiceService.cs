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
            string voiceId = "21m00Tcm4TlvDq8ikWAM"; // Rachel voice
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

    public async IAsyncEnumerable<byte[]> TextToSpeechStreamAsync(string text)
    {
        // ElevenLabs streaming text-to-speech API
        string voiceId = "21m00Tcm4TlvDq8ikWAM"; // Rachel voice
        // Use MP3 streaming (free tier supports this, client will decode to PCM)
        string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}/stream?optimize_streaming_latency=3&output_format=mp3_44100_128";

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

        HttpResponseMessage? response = null;
        Stream? audioStream = null;

        // Send request with ResponseHeadersRead to enable streaming
        response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("⏱️ ElevenLabs streaming TTS failed with status: {StatusCode}, response: {ErrorBody}",
                response.StatusCode, errorBody);
            response.Dispose();
            yield break;
        }

        _logger.LogInformation("⏱️ Starting streaming TTS for text: {Text}",
            text.Substring(0, Math.Min(50, text.Length)));

        // Read stream in chunks
        audioStream = await response.Content.ReadAsStreamAsync();
        byte[] buffer = new byte[8192]; // 8KB chunks
        int bytesRead;
        int totalBytes = 0;
        int chunkCount = 0;

        while ((bytesRead = await audioStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            chunkCount++;
            totalBytes += bytesRead;

            // Create a new array with only the bytes read
            byte[] chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);

            if (chunkCount == 1)
            {
                _logger.LogInformation("⏱️ First audio chunk received ({ChunkSize} bytes)", bytesRead);
            }

            yield return chunk;
        }

        _logger.LogInformation("⏱️ Streaming TTS complete: {TotalBytes} bytes in {ChunkCount} chunks",
            totalBytes, chunkCount);

        audioStream?.Dispose();
        response?.Dispose();
    }

    public async Task<string> SpeechToTextAsync(byte[] audioData)
    {
        try
        {
            // Create a temporary stream from the audio data
            using MemoryStream audioStream = new MemoryStream(audioData);

            // Use OpenAI Whisper for speech-to-text
            ClientResult<AudioTranscription>? transcription = await _audioClient.TranscribeAudioAsync(audioStream, "audio.wav");
            string? text = transcription.Value.Text;

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