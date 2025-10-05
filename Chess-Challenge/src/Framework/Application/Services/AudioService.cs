using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class AudioService : IDisposable
    {
        private Music? currentMusic;
        private bool isPlaying;
        private string? tempAudioFilePath;

        // Streaming audio state
        private List<byte> streamingAudioBuffer = new();
        private bool isStreamingActive = false;
        private bool hasStartedPlayback = false;
        private FileStream? streamingFileStream;
        private string? streamingTempFilePath;
        private const int MinBufferSizeToStart = 8192; // Start playback after first chunk

        public bool IsPlaying => isPlaying;

        public async Task PlayBase64AudioAsync(string base64Audio)
        {
            if (string.IsNullOrEmpty(base64Audio))
            {
                Log("⚠️ Cannot play audio - empty base64 string", false, ConsoleColor.Yellow);
                return;
            }

            try
            {
                // Stop any currently playing audio
                StopAudio();

                // Decode base64 to bytes
                byte[] audioBytes = Convert.FromBase64String(base64Audio);
                Log($"🔊 Decoded audio: {audioBytes.Length} bytes", false, ConsoleColor.Cyan);

                // Create temp file (ElevenLabs returns MP3)
                tempAudioFilePath = Path.Combine(Path.GetTempPath(), $"tts_{Guid.NewGuid()}.mp3");
                await File.WriteAllBytesAsync(tempAudioFilePath, audioBytes);

                // Load and play music stream in a background task
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (!Raylib.IsAudioDeviceReady())
                        {
                            Log("⚠️ Audio device not ready", false, ConsoleColor.Yellow);
                            return;
                        }

                        Music music = Raylib.LoadMusicStream(tempAudioFilePath);
                        music.looping = false; // Don't loop the audio
                        Raylib.SetMusicVolume(music, 1.0f);
                        currentMusic = music;
                        
                        Raylib.PlayMusicStream(currentMusic.Value);
                        isPlaying = true;
                        Log("🔊 Playing TTS audio", false, ConsoleColor.Green);

                        // Update music stream in background
                        while (isPlaying && Raylib.IsMusicStreamPlaying(currentMusic.Value))
                        {
                            Raylib.UpdateMusicStream(currentMusic.Value);
                            System.Threading.Thread.Sleep(10);
                        }

                        // Cleanup after playback
                        StopAudio();
                        Log("✅ TTS audio playback complete", false, ConsoleColor.Green);
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ Error during audio playback: {ex.Message}", false, ConsoleColor.Red);
                        StopAudio();
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to play audio: {ex.Message}", false, ConsoleColor.Red);
                CleanupTempFile();
            }
        }

        public void StopAudio()
        {
            if (currentMusic.HasValue)
            {
                try
                {
                    if (Raylib.IsMusicStreamPlaying(currentMusic.Value))
                    {
                        Raylib.StopMusicStream(currentMusic.Value);
                    }
                    Raylib.UnloadMusicStream(currentMusic.Value);
                    currentMusic = null;
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Error stopping audio: {ex.Message}", false, ConsoleColor.Yellow);
                }
            }

            isPlaying = false;
            CleanupTempFile();
        }

        private void CleanupTempFile()
        {
            if (!string.IsNullOrEmpty(tempAudioFilePath) && File.Exists(tempAudioFilePath))
            {
                try
                {
                    File.Delete(tempAudioFilePath);
                    tempAudioFilePath = null;
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Failed to delete temp audio file: {ex.Message}", false, ConsoleColor.Yellow);
                }
            }
        }

        public void StartStreamingAudio()
        {
            // Stop any currently playing audio
            StopAudio();
            StopStreamingAudio();

            // Initialize streaming state
            isStreamingActive = true;
            hasStartedPlayback = false;
            streamingAudioBuffer.Clear();

            // Create temp file for streaming
            streamingTempFilePath = Path.Combine(Path.GetTempPath(), $"tts_stream_{Guid.NewGuid()}.mp3");
            streamingFileStream = new FileStream(streamingTempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);

            Log("🔊 Started streaming audio session", false, ConsoleColor.Cyan);
        }

        public async Task AddAudioChunk(byte[] chunkBytes)
        {
            if (!isStreamingActive || streamingFileStream == null)
            {
                Log("⚠️ Cannot add audio chunk - streaming not active", false, ConsoleColor.Yellow);
                return;
            }

            try
            {
                // Write chunk to file immediately
                await streamingFileStream.WriteAsync(chunkBytes, 0, chunkBytes.Length);
                await streamingFileStream.FlushAsync();

                streamingAudioBuffer.AddRange(chunkBytes);

                // Start playback after receiving first chunk
                if (!hasStartedPlayback && streamingAudioBuffer.Count >= MinBufferSizeToStart)
                {
                    hasStartedPlayback = true;
                    _ = Task.Run(() => StartStreamPlayback());
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error adding audio chunk: {ex.Message}", false, ConsoleColor.Red);
            }
        }

        public void FinishStreamingAudio()
        {
            if (!isStreamingActive)
            {
                return;
            }

            try
            {
                // Close the file stream so Raylib can read it
                streamingFileStream?.Close();
                streamingFileStream?.Dispose();
                streamingFileStream = null;

                isStreamingActive = false;

                Log($"✅ Streaming audio finished: {streamingAudioBuffer.Count} bytes total", false, ConsoleColor.Green);

                // If we never started playback (very small audio), start it now
                if (!hasStartedPlayback && streamingAudioBuffer.Count > 0)
                {
                    _ = Task.Run(() => StartStreamPlayback());
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error finishing streaming audio: {ex.Message}", false, ConsoleColor.Red);
                CleanupStreamingFile();
            }
        }

        private void StartStreamPlayback()
        {
            if (string.IsNullOrEmpty(streamingTempFilePath) || !File.Exists(streamingTempFilePath))
            {
                Log("⚠️ Cannot start stream playback - file not found", false, ConsoleColor.Yellow);
                return;
            }

            try
            {
                if (!Raylib.IsAudioDeviceReady())
                {
                    Log("⚠️ Audio device not ready", false, ConsoleColor.Yellow);
                    return;
                }

                // Wait a bit for file to be readable
                System.Threading.Thread.Sleep(50);

                Music music = Raylib.LoadMusicStream(streamingTempFilePath);
                music.looping = false;
                Raylib.SetMusicVolume(music, 1.0f);
                currentMusic = music;

                Raylib.PlayMusicStream(currentMusic.Value);
                isPlaying = true;
                Log("🔊 Started streaming playback", false, ConsoleColor.Green);

                // Update music stream in background
                while (isPlaying && Raylib.IsMusicStreamPlaying(currentMusic.Value))
                {
                    Raylib.UpdateMusicStream(currentMusic.Value);
                    System.Threading.Thread.Sleep(10);
                }

                // Cleanup after playback
                StopAudio();
                CleanupStreamingFile();
                Log("✅ Streaming playback complete", false, ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                Log($"❌ Error during streaming playback: {ex.Message}", false, ConsoleColor.Red);
                StopAudio();
                CleanupStreamingFile();
            }
        }

        private void StopStreamingAudio()
        {
            isStreamingActive = false;
            hasStartedPlayback = false;

            streamingFileStream?.Close();
            streamingFileStream?.Dispose();
            streamingFileStream = null;

            streamingAudioBuffer.Clear();
        }

        private void CleanupStreamingFile()
        {
            if (!string.IsNullOrEmpty(streamingTempFilePath) && File.Exists(streamingTempFilePath))
            {
                try
                {
                    File.Delete(streamingTempFilePath);
                    streamingTempFilePath = null;
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Failed to delete streaming temp file: {ex.Message}", false, ConsoleColor.Yellow);
                }
            }
        }

        public void Dispose()
        {
            StopAudio();
            StopStreamingAudio();
            CleanupStreamingFile();
        }
    }
}
