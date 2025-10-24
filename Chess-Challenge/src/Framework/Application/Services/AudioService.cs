using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Audio.OpenAL;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application.Services
{
    public class AudioService : IDisposable
    {
        private Music? currentMusic;
        private bool isPlaying;
        private string? tempAudioFilePath;

        // Recording state (OpenAL cross-platform)
        private ALCaptureDevice captureDevice;
        private List<short> recordedSamples;
        private bool isRecording;
        private DateTime recordingStartTime;
        private Thread? recordingThread;
        private bool shouldStopRecording;
        private const int SampleRate = 16000; // 16kHz for Whisper
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
                // Get default capture device
                string? deviceName = ALC.GetString(ALDevice.Null, AlcGetString.CaptureDefaultDeviceSpecifier);

                if (string.IsNullOrEmpty(deviceName))
                {
                    Log("❌ No microphone device found", false, ConsoleColor.Red);
                    return false;
                }

                Log($"🎤 Opening capture device: {deviceName}", false, ConsoleColor.Cyan);

                // Open capture device: 16kHz, mono, 16-bit
                captureDevice = ALC.CaptureOpenDevice(deviceName, SampleRate, ALFormat.Mono16, SampleRate / 2);

                if (captureDevice == null || captureDevice.Handle == IntPtr.Zero)
                {
                    Log($"❌ Failed to open capture device", false, ConsoleColor.Red);
                    return false;
                }

                // Start capturing
                recordedSamples = new List<short>();
                isRecording = true;
                shouldStopRecording = false;
                recordingStartTime = DateTime.UtcNow;
                ALC.CaptureStart(captureDevice);

                // Start recording thread
                recordingThread = new Thread(RecordingThreadLoop);
                recordingThread.Start();

                Log("🎤 Started recording (OpenAL)", false, ConsoleColor.Green);
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to start recording: {ex.Message}", false, ConsoleColor.Red);
                CleanupRecording();
                return false;
            }
        }

        private void RecordingThreadLoop()
        {
            const int bufferSize = 4096; // samples to read at a time
            short[] buffer = new short[bufferSize];

            try
            {
                while (!shouldStopRecording && isRecording)
                {
                    // Check if max duration reached
                    if (RecordingDuration >= MaxRecordingSeconds)
                    {
                        Log($"⏱️ Max recording duration ({MaxRecordingSeconds}s) reached, stopping", false, ConsoleColor.Yellow);
                        break;
                    }

                    // Get number of samples available
                    ALC.GetInteger(captureDevice, AlcGetInteger.CaptureSamples, 1, out int samplesAvailable);

                    if (samplesAvailable > 0)
                    {
                        int samplesToRead = Math.Min(samplesAvailable, bufferSize);
                        ALC.CaptureSamples(captureDevice, buffer, samplesToRead);

                        // Add to recorded samples
                        for (int i = 0; i < samplesToRead; i++)
                        {
                            recordedSamples.Add(buffer[i]);
                        }

                        // Calculate volume (peak amplitude)
                        float peak = 0f;
                        for (int i = 0; i < samplesToRead; i++)
                        {
                            float sampleValue = buffer[i] / 32768f;
                            float absSample = Math.Abs(sampleValue);
                            if (absSample > peak)
                            {
                                peak = absSample;
                            }
                        }

                        // Smooth volume
                        currentVolume = currentVolume * 0.7f + peak * 0.3f;
                    }

                    Thread.Sleep(10); // Check every 10ms
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Recording thread error: {ex.Message}", false, ConsoleColor.Red);
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
                // Signal recording thread to stop
                shouldStopRecording = true;
                isRecording = false;

                // Wait for thread to finish
                recordingThread?.Join(1000);

                // Stop capture
                if (captureDevice != null && captureDevice.Handle != IntPtr.Zero)
                {
                    ALC.CaptureStop(captureDevice);

                    // Get any remaining samples
                    ALC.GetInteger(captureDevice, AlcGetInteger.CaptureSamples, 1, out int samplesAvailable);
                    if (samplesAvailable > 0)
                    {
                        short[] buffer = new short[samplesAvailable];
                        ALC.CaptureSamples(captureDevice, buffer, samplesAvailable);
                        recordedSamples.AddRange(buffer);
                    }
                }

                if (recordedSamples.Count == 0)
                {
                    Log("⚠️ Recording is empty", false, ConsoleColor.Yellow);
                    CleanupRecording();
                    return null;
                }

                // Create WAV file
                string tempWavPath = Path.Combine(Path.GetTempPath(), $"recording_{Guid.NewGuid()}.wav");
                WriteWavFile(tempWavPath, recordedSamples.ToArray(), SampleRate);

                double duration = (DateTime.UtcNow - recordingStartTime).TotalSeconds;
                int bytesWritten = recordedSamples.Count * 2; // 16-bit samples
                Log($"🎤 Stopped recording ({duration:F1}s, {bytesWritten} bytes) -> {tempWavPath}", false, ConsoleColor.Green);

                CleanupRecording();
                return tempWavPath;
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to stop recording: {ex.Message}", false, ConsoleColor.Red);
                CleanupRecording();
                return null;
            }
        }

        private void WriteWavFile(string filePath, short[] samples, int sampleRate)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                int numChannels = 1; // Mono
                int bitsPerSample = 16;
                int byteRate = sampleRate * numChannels * bitsPerSample / 8;
                int blockAlign = numChannels * bitsPerSample / 8;
                int dataSize = samples.Length * sizeof(short);

                // RIFF header
                writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize); // File size - 8
                writer.Write(new char[4] { 'W', 'A', 'V', 'E' });

                // fmt chunk
                writer.Write(new char[4] { 'f', 'm', 't', ' ' });
                writer.Write(16); // fmt chunk size
                writer.Write((short)1); // Audio format (1 = PCM)
                writer.Write((short)numChannels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)bitsPerSample);

                // data chunk
                writer.Write(new char[4] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                // Write samples
                foreach (short sample in samples)
                {
                    writer.Write(sample);
                }
            }
        }

        private void CleanupRecording()
        {
            try
            {
                shouldStopRecording = true;
                isRecording = false;

                if (recordingThread != null && recordingThread.IsAlive)
                {
                    recordingThread.Join(1000);
                    recordingThread = null;
                }

                if (captureDevice != null && captureDevice.Handle != IntPtr.Zero)
                {
                    try
                    {
                        ALC.CaptureStop(captureDevice);
                        ALC.CaptureCloseDevice(captureDevice);
                    }
                    catch { }
                    captureDevice = default;
                }

                recordedSamples?.Clear();
                currentVolume = 0f;
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
