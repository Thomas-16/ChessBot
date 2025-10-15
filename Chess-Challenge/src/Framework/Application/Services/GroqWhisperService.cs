using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class GroqWhisperService : IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly string apiKey;
        private readonly string model;
        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        public GroqWhisperService(string apiKey, string model = "whisper-large-v3")
        {
            this.apiKey = apiKey;
            this.model = model;
            this.httpClient = new HttpClient();
            this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_GROQ_API_KEY_HERE")
            {
                isInitialized = true;
                Log("✅ Groq Whisper service initialized", false, ConsoleColor.Green);
            }
            else
            {
                Log("⚠️ Groq API key not configured - Whisper service disabled", false, ConsoleColor.Yellow);
            }
        }

        public async Task<string?> TranscribeAsync(string audioFilePath)
        {
            if (!isInitialized)
            {
                Log("⚠️ Groq Whisper not initialized", false, ConsoleColor.Yellow);
                return null;
            }

            try
            {
                Log($"🎤 Transcribing audio with Groq Whisper: {audioFilePath}", false, ConsoleColor.Cyan);
                DateTime startTime = DateTime.UtcNow;

                // Read audio file
                byte[] audioBytes = await File.ReadAllBytesAsync(audioFilePath);

                // Create multipart form data
                using var content = new MultipartFormDataContent();
                using var audioContent = new ByteArrayContent(audioBytes);
                audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                content.Add(audioContent, "file", Path.GetFileName(audioFilePath));
                content.Add(new StringContent(model), "model");
                content.Add(new StringContent("json"), "response_format");

                // Send request to Groq
                var response = await httpClient.PostAsync("https://api.groq.com/openai/v1/audio/transcriptions", content);
                response.EnsureSuccessStatusCode();

                // Parse response
                string responseBody = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(responseBody);
                string transcription = doc.RootElement.GetProperty("text").GetString() ?? "";

                double elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                Log($"✅ Groq transcription complete ({elapsedMs:F0}ms): {transcription}", false, ConsoleColor.Green);

                return transcription.Trim();
            }
            catch (Exception ex)
            {
                Log($"❌ Groq transcription failed: {ex.Message}", false, ConsoleColor.Red);
                return null;
            }
        }

        public void Dispose()
        {
            httpClient?.Dispose();
            Log("🔄 Groq Whisper service disposed", false, ConsoleColor.Gray);
        }
    }
}
