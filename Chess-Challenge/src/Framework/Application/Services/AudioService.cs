using Raylib_cs;
using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class AudioService : IDisposable
    {
        private Music? currentMusic;
        private bool isPlaying;
        private string? tempAudioFilePath;

        // Recording state
        private WaveInEvent? waveIn;
        private MemoryStream? recordingStream;
        private WaveFileWriter? waveWriter;
        private bool isRecording;
        private DateTime recordingStartTime;
        private const int MaxRecordingSeconds = 30;
        private float currentVolume = 0f; // 0.0 to 1.0

        public bool IsPlaying => isPlaying;
        public bool IsRecording => isRecording;
        public double RecordingDuration => isRecording ? (DateTime.UtcNow - recordingStartTime).TotalSeconds : 0;
        public float CurrentVolume => currentVolume;

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

        public bool StartRecording()
        {
            if (isRecording)
            {
                Log("⚠️ Already recording", false, ConsoleColor.Yellow);
                return false;
            }

            try
            {
                // Check if microphone is available
                if (WaveInEvent.DeviceCount == 0)
                {
                    Log("❌ No microphone device found", false, ConsoleColor.Red);
                    return false;
                }

                // Initialize recording
                waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 1) // 16kHz, mono (Whisper-optimized)
                };

                recordingStream = new MemoryStream();
                waveWriter = new WaveFileWriter(recordingStream, waveIn.WaveFormat);

                waveIn.DataAvailable += OnDataAvailable;
                waveIn.RecordingStopped += OnRecordingStopped;

                waveIn.StartRecording();
                isRecording = true;
                recordingStartTime = DateTime.UtcNow;

                Log("🎤 Started recording", false, ConsoleColor.Green);
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to start recording: {ex.Message}", false, ConsoleColor.Red);
                CleanupRecording();
                return false;
            }
        }

        public string? StopRecording()
        {
            if (!isRecording)
            {
                Log("⚠️ Not currently recording", false, ConsoleColor.Yellow);
                return null;
            }

            try
            {
                waveIn?.StopRecording();
                isRecording = false;

                // Wait for recording to fully stop
                System.Threading.Thread.Sleep(100);

                // Finalize WAV file
                waveWriter?.Flush();

                // Get audio bytes
                byte[] audioBytes = recordingStream?.ToArray() ?? Array.Empty<byte>();

                if (audioBytes.Length == 0)
                {
                    Log("⚠️ Recording is empty", false, ConsoleColor.Yellow);
                    CleanupRecording();
                    return null;
                }

                // Convert to base64
                string base64Audio = Convert.ToBase64String(audioBytes);

                double duration = (DateTime.UtcNow - recordingStartTime).TotalSeconds;
                Log($"🎤 Stopped recording ({duration:F1}s, {audioBytes.Length} bytes)", false, ConsoleColor.Green);

                CleanupRecording();
                return base64Audio;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to stop recording: {ex.Message}", false, ConsoleColor.Red);
                CleanupRecording();
                return null;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (waveWriter != null && e.BytesRecorded > 0)
            {
                waveWriter.Write(e.Buffer, 0, e.BytesRecorded);

                // Calculate volume level (peak amplitude) for visualization
                float peak = 0f;
                // Assuming 16-bit PCM audio (2 bytes per sample)
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    if (i + 1 < e.BytesRecorded)
                    {
                        short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                        float sampleValue = sample / 32768f; // Normalize to -1.0 to 1.0
                        float absSample = Math.Abs(sampleValue);
                        if (absSample > peak)
                        {
                            peak = absSample;
                        }
                    }
                }

                // Smooth the volume with exponential moving average
                currentVolume = currentVolume * 0.7f + peak * 0.3f;

                // Auto-stop if max duration reached
                if (RecordingDuration >= MaxRecordingSeconds)
                {
                    Log($"⏱️ Max recording duration ({MaxRecordingSeconds}s) reached, stopping", false, ConsoleColor.Yellow);
                    waveIn?.StopRecording();
                }
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Log($"❌ Recording stopped with error: {e.Exception.Message}", false, ConsoleColor.Red);
            }
        }

        private void CleanupRecording()
        {
            try
            {
                if (waveWriter != null)
                {
                    waveWriter.Dispose();
                    waveWriter = null;
                }

                if (recordingStream != null)
                {
                    recordingStream.Dispose();
                    recordingStream = null;
                }

                if (waveIn != null)
                {
                    waveIn.DataAvailable -= OnDataAvailable;
                    waveIn.RecordingStopped -= OnRecordingStopped;
                    waveIn.Dispose();
                    waveIn = null;
                }

                isRecording = false;
                currentVolume = 0f; // Reset volume indicator
            }
            catch (Exception ex)
            {
                Log($"⚠️ Error cleaning up recording: {ex.Message}", false, ConsoleColor.Yellow);
            }
        }

        public void Dispose()
        {
            StopAudio();
            CleanupRecording();
        }
    }
}
