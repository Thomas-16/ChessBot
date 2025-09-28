using System;
using Raylib_cs;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ChessChallenge.Application
{
    static class Program
    {
        const bool hideRaylibLogs = true;
        static Camera2D cam;

        public static bool UCI_MODE = false;

        public static bool SIMPLIFIED_MODE = true;

        public static void Main(string[] args)
        {
            if (UCI_MODE)
            {
                RunUCIMode();
                return;
            }
            
            RunGUIMode();
        }
        
        static void RunUCIMode()
        {
            var uciEngine = new UCIEngine();
            uciEngine.Run();
        }
        
        static void RunGUIMode()
        {
            int screenWidth = 1920;
            int screenHeight = 1080;

            if (hideRaylibLogs)
            {
                unsafe
                {
                    Raylib.SetTraceLogCallback(&LogCustom);
                }
            }

            Raylib.InitWindow(screenWidth, screenHeight, "Chess AI");
            Raylib.SetWindowState(ConfigFlags.FLAG_WINDOW_RESIZABLE | ConfigFlags.FLAG_WINDOW_ALWAYS_RUN);
            Raylib.SetWindowMinSize(800, 600); // Minimum size to keep UI usable
            Raylib.SetTargetFPS(60);

            AudioManager.Instance.Initialize();

            UpdateCamera(screenWidth, screenHeight);

            ChallengeController controller = new();

            int lastWidth = Raylib.GetScreenWidth();
            int lastHeight = Raylib.GetScreenHeight();

            while (!Raylib.WindowShouldClose())
            {
                // Handle window resize - check every frame for smooth scaling
                int currentWidth = Raylib.GetScreenWidth();
                int currentHeight = Raylib.GetScreenHeight();

                if (currentWidth != lastWidth || currentHeight != lastHeight)
                {
                    UpdateCamera(currentWidth, currentHeight);
                    lastWidth = currentWidth;
                    lastHeight = currentHeight;
                }

                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(22, 22, 22, 255));
                Raylib.BeginMode2D(cam);

                controller.Update();
                controller.Draw();

                Raylib.EndMode2D();

                controller.DrawOverlay();

                Raylib.EndDrawing();
            }

            Raylib.CloseWindow();

            controller.Release();
            UIHelper.Release();
            AudioManager.Instance.Cleanup();
        }


        public static Vector2 ScreenToWorldPos(Vector2 screenPos) => Raylib.GetScreenToWorld2D(screenPos, cam);

        static void UpdateCamera(int screenWidth, int screenHeight)
        {
            cam = new Camera2D();
            cam.target = new Vector2(0, 15);
            cam.offset = new Vector2(screenWidth / 2f, screenHeight / 2f);

            // Better scaling that considers both width and height
            // Use the smaller scaling factor to ensure everything fits
            float widthScale = screenWidth / 1280f;
            float heightScale = screenHeight / 720f;
            float scale = Math.Min(widthScale, heightScale);

            // Apply a reasonable zoom factor
            cam.zoom = scale * 0.7f;
        }


        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static unsafe void LogCustom(int logLevel, sbyte* text, sbyte* args)
        {
        }

    }


}