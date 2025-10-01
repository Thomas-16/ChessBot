using System;
using Raylib_cs;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ChessChallenge.Application.Config;
using ChessChallenge.Application.Services;

namespace ChessChallenge.Application
{
    static class Program
    {
        const bool hideRaylibLogs = true;
        static Camera2D cam;

        public static bool UCI_MODE = false;
        public static bool SIMPLIFIED_MODE = true;

        // Dependency injection services
        public static IServiceProvider? ServiceProvider { get; private set; }
        public static GameStorageService? GameStorage { get; private set; }

        public static void Main(string[] args)
        {
            if (UCI_MODE)
            {
                RunUCIMode();
                return;
            }
            
            // Initialize services
            InitializeServices();
            
            RunGUIMode();
        }

        static void InitializeServices()
        {
            try
            {
                // Build configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                // Load backend configuration
                var backendConfig = BackendConfig.LoadFromConfiguration(configuration);

                // Setup dependency injection
                var services = new ServiceCollection();
                services.AddSingleton(backendConfig);
                services.AddSingleton<SignalRService>();
                services.AddHttpClient<BackendApiService>();
                services.AddSingleton<GameStorageService>();
                services.AddSingleton<AudioService>();

                ServiceProvider = services.BuildServiceProvider();
                GameStorage = ServiceProvider.GetRequiredService<GameStorageService>();

                ConsoleHelper.Log("🔧 Services initialized successfully", false, ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                ConsoleHelper.Log($"⚠️ Failed to initialize services: {ex.Message}", false, ConsoleColor.Yellow);
                ConsoleHelper.Log("🎮 Continuing in offline mode", false, ConsoleColor.Cyan);
            }
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
            Raylib.SetWindowMinSize(800, 600);
            Raylib.SetTargetFPS(60);

            AudioManager.Instance.Initialize();

            UpdateCamera(screenWidth, screenHeight);

            ChallengeController controller = new();

            // Initialize backend connection asynchronously
            if (GameStorage != null)
            {
                ConsoleHelper.Log("🚀 Starting backend connection...", false, ConsoleColor.Cyan);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var connected = await GameStorage.InitializeConnectionAsync();
                        ConsoleHelper.Log($"🔗 Backend connection result: {connected}", false, connected ? ConsoleColor.Green : ConsoleColor.Yellow);
                    }
                    catch (Exception ex)
                    {
                        ConsoleHelper.Log($"❌ Backend connection error: {ex.Message}", false, ConsoleColor.Red);
                    }
                });
            }
            else
            {
                ConsoleHelper.Log("⚠️ GameStorage is null - no backend integration", false, ConsoleColor.Yellow);
            }

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
            GameStorage?.DisconnectAsync();
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