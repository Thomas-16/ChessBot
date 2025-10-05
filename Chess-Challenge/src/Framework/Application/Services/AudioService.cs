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

        // Streaming audio state (using AudioStream for true streaming)
        private AudioStream? streamingAudioStream;
        private bool isStreamingActive = false;
        private bool isStreamingPlaying = false;
        private Queue<byte[]> audioChunkQueue = new();
        private const int SampleRate = 44100;
        private const int Channels = 1; // Mono
        private const int MinBufferSizeToStart = 8192; // Start playback after buffering first chunk

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

            if (!Raylib.IsAudioDeviceReady())
            {
                Log("⚠️ Audio device not ready", false, ConsoleColor.Yellow);
                return;
            }

            try
            {
                // Create AudioStream for true streaming (PCM 16-bit, 44100Hz, Mono)
                unsafe
                {
                    streamingAudioStream = Raylib.LoadAudioStream((uint)SampleRate, 16, (uint)Channels);
                }

                isStreamingActive = true;
                isStreamingPlaying = false;
                audioChunkQueue.Clear();

                Log("🔊 Started streaming audio session", false, ConsoleColor.Cyan);
            }
            catch (Exception ex)
            {
                Log($"❌ Error starting streaming audio: {ex.Message}", false, ConsoleColor.Red);
            }
        }

        public void AddAudioChunk(byte[] chunkBytes)
        {
            if (!isStreamingActive)
            {
                Log("⚠️ Cannot add audio chunk - streaming not active", false, ConsoleColor.Yellow);
                return;
            }

            try
            {
                // Queue the chunk
                audioChunkQueue.Enqueue(chunkBytes);

                // Start playback after receiving first chunk
                if (!isStreamingPlaying && audioChunkQueue.Count > 0)
                {
                    if (streamingAudioStream.HasValue)
                    {
                        Raylib.PlayAudioStream(streamingAudioStream.Value);
                        isStreamingPlaying = true;
                        Log($"🔊 Started streaming playback with first chunk ({chunkBytes.Length} bytes)", false, ConsoleColor.Green);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error adding audio chunk: {ex.Message}", false, ConsoleColor.Red);
            }
        }

        public void UpdateStreamingAudio()
        {
            if (!isStreamingActive || !streamingAudioStream.HasValue || !isStreamingPlaying)
            {
                return;
            }

            try
            {
                // Check if audio stream needs more data
                if (Raylib.IsAudioStreamProcessed(streamingAudioStream.Value) && audioChunkQueue.Count > 0)
                {
                    byte[] chunk = audioChunkQueue.Dequeue();

                    unsafe
                    {
                        fixed (byte* dataPtr = chunk)
                        {
                            Raylib.UpdateAudioStream(streamingAudioStream.Value, dataPtr, chunk.Length / 2); // PCM 16-bit = 2 bytes per sample
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error updating streaming audio: {ex.Message}", false, ConsoleColor.Red);
            }
        }

        public void FinishStreamingAudio()
        {
            if (!isStreamingActive)
            {
                return;
            }

            Log($"✅ Streaming audio finished, {audioChunkQueue.Count} chunks remaining in queue", false, ConsoleColor.Green);

            // Mark as inactive but keep playing queued chunks
            isStreamingActive = false;
        }

        private void StopStreamingAudio()
        {
            if (streamingAudioStream.HasValue)
            {
                try
                {
                    if (isStreamingPlaying)
                    {
                        Raylib.StopAudioStream(streamingAudioStream.Value);
                    }
                    Raylib.UnloadAudioStream(streamingAudioStream.Value);
                    streamingAudioStream = null;
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Error stopping streaming audio: {ex.Message}", false, ConsoleColor.Yellow);
                }
            }

            isStreamingActive = false;
            isStreamingPlaying = false;
            audioChunkQueue.Clear();
        }

        public void Dispose()
        {
            StopAudio();
            StopStreamingAudio();
        }
    }
}
