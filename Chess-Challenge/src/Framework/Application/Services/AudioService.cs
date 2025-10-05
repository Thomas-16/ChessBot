using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NLayer;
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
        private Queue<float[]> pcmChunkQueue = new(); // Decoded PCM samples
        private MemoryStream mp3Buffer = new(); // Accumulates MP3 chunks for decoding
        private MpegFile? mp3Decoder;
        private const int SampleRate = 44100;
        private const int Channels = 1; // Mono
        private const int PcmChunkSize = 4096; // PCM samples per update

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
                pcmChunkQueue.Clear();
                mp3Buffer = new MemoryStream();

                Log("🔊 Started streaming audio session", false, ConsoleColor.Cyan);
            }
            catch (Exception ex)
            {
                Log($"❌ Error starting streaming audio: {ex.Message}", false, ConsoleColor.Red);
            }
        }

        public void AddAudioChunk(byte[] mp3ChunkBytes)
        {
            if (!isStreamingActive)
            {
                Log("⚠️ Cannot add audio chunk - streaming not active", false, ConsoleColor.Yellow);
                return;
            }

            try
            {
                // Append MP3 chunk to buffer
                mp3Buffer.Write(mp3ChunkBytes, 0, mp3ChunkBytes.Length);

                // Try to decode accumulated MP3 data to PCM
                mp3Buffer.Position = 0;

                try
                {
                    if (mp3Decoder == null)
                    {
                        mp3Decoder = new MpegFile(mp3Buffer);
                    }

                    // Decode PCM samples from MP3
                    float[] pcmSamples = new float[PcmChunkSize];
                    int samplesRead = mp3Decoder.ReadSamples(pcmSamples, 0, PcmChunkSize);

                    if (samplesRead > 0)
                    {
                        // Trim array to actual samples read
                        float[] trimmedSamples = new float[samplesRead];
                        Array.Copy(pcmSamples, trimmedSamples, samplesRead);

                        pcmChunkQueue.Enqueue(trimmedSamples);

                        // Start playback after decoding first chunk
                        if (!isStreamingPlaying && pcmChunkQueue.Count > 0)
                        {
                            if (streamingAudioStream.HasValue)
                            {
                                Raylib.PlayAudioStream(streamingAudioStream.Value);
                                isStreamingPlaying = true;
                                Log($"🔊 Started streaming playback", false, ConsoleColor.Green);
                            }
                        }
                    }
                }
                catch
                {
                    // MP3 decoding failed (incomplete frame), wait for more data
                    mp3Buffer.Position = mp3Buffer.Length;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error adding audio chunk: {ex.Message}", false, ConsoleColor.Red);
            }
        }

        public void UpdateStreamingAudio()
        {
            if (!streamingAudioStream.HasValue || !isStreamingPlaying)
            {
                return;
            }

            try
            {
                // Check if audio stream needs more data
                if (Raylib.IsAudioStreamProcessed(streamingAudioStream.Value) && pcmChunkQueue.Count > 0)
                {
                    float[] pcmSamples = pcmChunkQueue.Dequeue();

                    // Convert float samples to 16-bit PCM for Raylib
                    short[] pcm16Bit = new short[pcmSamples.Length];
                    for (int i = 0; i < pcmSamples.Length; i++)
                    {
                        pcm16Bit[i] = (short)(pcmSamples[i] * 32767f);
                    }

                    unsafe
                    {
                        fixed (short* dataPtr = pcm16Bit)
                        {
                            Raylib.UpdateAudioStream(streamingAudioStream.Value, dataPtr, pcm16Bit.Length);
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

            Log($"✅ Streaming audio finished, {pcmChunkQueue.Count} PCM chunks remaining in queue", false, ConsoleColor.Green);

            // Mark as inactive but keep playing queued chunks
            isStreamingActive = false;

            // Cleanup MP3 decoder
            mp3Decoder = null;
            mp3Buffer?.Dispose();
            mp3Buffer = new MemoryStream();
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
            pcmChunkQueue.Clear();

            mp3Decoder = null;
            mp3Buffer?.Dispose();
            mp3Buffer = new MemoryStream();
        }

        public void Dispose()
        {
            StopAudio();
            StopStreamingAudio();
        }
    }
}
