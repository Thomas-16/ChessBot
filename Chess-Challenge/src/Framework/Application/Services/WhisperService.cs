using System;
using System.IO;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class WhisperService : IDisposable
    {
        private WhisperProcessor? processor;
        private readonly string modelPath = "resources/Models/ggml-base.bin";
        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        public async Task<bool> InitializeAsync()
        {
            if (isInitialized)
            {
                Log("✅ Whisper already initialized", false, ConsoleColor.Green);
                return true;
            }

            try
            {
                Log("🔄 Initializing Whisper.NET...", false, ConsoleColor.Cyan);

                // Create models directory if it doesn't exist
                string modelDir = Path.GetDirectoryName(modelPath) ?? "resources/Models";
                Directory.CreateDirectory(modelDir);

                // Download model if it doesn't exist
                if (!File.Exists(modelPath))
                {
                    Log($"📥 Downloading Whisper base model (approx 140MB)...", false, ConsoleColor.Yellow);

                    using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Base);
                    using var fileWriter = File.OpenWrite(modelPath);
                    await modelStream.CopyToAsync(fileWriter);

                    Log($"✅ Model downloaded to {modelPath}", false, ConsoleColor.Green);
                }

                // Load the model
                var factory = WhisperFactory.FromPath(modelPath);
                processor = factory.CreateBuilder()
                    .WithLanguage("en")
                    .Build();

                isInitialized = true;
                Log("✅ Whisper initialized successfully", false, ConsoleColor.Green);
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to initialize Whisper: {ex.Message}", false, ConsoleColor.Red);
                return false;
            }
        }

        public async Task<string?> TranscribeAsync(string audioFilePath)
        {
            if (!isInitialized || processor == null)
            {
                Log("⚠️ Whisper not initialized", false, ConsoleColor.Yellow);
                return null;
            }

            try
            {
                Log($"🎤 Transcribing audio file: {audioFilePath}", false, ConsoleColor.Cyan);
                DateTime startTime = DateTime.UtcNow;

                string transcription = "";

                using (var fileStream = File.OpenRead(audioFilePath))
                {
                    await foreach (var segment in processor.ProcessAsync(fileStream))
                    {
                        transcription += segment.Text;
                    }
                }

                double elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                Log($"✅ Transcription complete ({elapsedMs:F0}ms): {transcription}", false, ConsoleColor.Green);

                return transcription.Trim();
            }
            catch (Exception ex)
            {
                Log($"❌ Transcription failed: {ex.Message}", false, ConsoleColor.Red);
                return null;
            }
        }

        public void Dispose()
        {
            processor?.Dispose();
            isInitialized = false;
            Log("🔄 Whisper service disposed", false, ConsoleColor.Gray);
        }
    }
}
