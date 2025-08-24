using Raylib_cs;
using System;
using System.IO;

namespace ChessChallenge.Application
{
    public class AudioManager
    {
        private static AudioManager? instance;
        public static AudioManager Instance => instance ??= new AudioManager();

        private Sound moveSound;
        private Sound captureSound;
        private bool isInitialized;

        private AudioManager() { }

        public void Initialize()
        {
            try
            {
                Raylib.InitAudioDevice();
                
                if (!Raylib.IsAudioDeviceReady())
                {
                    Console.WriteLine("Warning: Audio device could not be initialized");
                    return;
                }

                LoadSounds();
                isInitialized = true;
                Console.WriteLine("Audio system initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to initialize audio system: {ex.Message}");
                isInitialized = false;
            }
        }

        private void LoadSounds()
        {
            string moveSoundPath = FileHelper.GetResourcePath("Sfx", "Move.wav");
            string captureSoundPath = FileHelper.GetResourcePath("Sfx", "Capture.wav");

            if (File.Exists(moveSoundPath))
            {
                moveSound = Raylib.LoadSound(moveSoundPath);
            }
            else
            {
                Console.WriteLine($"Warning: Move sound file not found at {moveSoundPath}");
            }

            if (File.Exists(captureSoundPath))
            {
                captureSound = Raylib.LoadSound(captureSoundPath);
            }
            else
            {
                Console.WriteLine($"Warning: Capture sound file not found at {captureSoundPath}");
            }
        }

        public void PlayMoveSound()
        {
            if (isInitialized && Settings.EnableSounds)
            {
                Raylib.PlaySound(moveSound);
            }
        }

        public void PlayCaptureSound()
        {
            if (isInitialized && Settings.EnableSounds)
            {
                Raylib.PlaySound(captureSound);
            }
        }

        public void Cleanup()
        {
            if (isInitialized)
            {
                try
                {
                    Raylib.UnloadSound(moveSound);
                    Raylib.UnloadSound(captureSound);
                    Raylib.CloseAudioDevice();
                    Console.WriteLine("Audio system cleaned up");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error during audio cleanup: {ex.Message}");
                }
                finally
                {
                    isInitialized = false;
                }
            }
        }
    }
}