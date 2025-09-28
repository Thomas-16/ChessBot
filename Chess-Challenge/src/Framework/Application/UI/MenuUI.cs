using Raylib_cs;
using System.Numerics;
using System;
using System.IO;

namespace ChessChallenge.Application
{
    public static class MenuUI
    {
        public static void DrawButtons(ChallengeController controller)
        {
            Vector2 buttonPos = UIHelper.Scale(new Vector2(260, 210));
            Vector2 buttonSize = UIHelper.Scale(new Vector2(260, 55));
            float spacing = buttonSize.Y * 1.2f;
            float breakSpacing = spacing * 0.6f;

            // Game Buttons
            if (NextButtonInRow(Program.SIMPLIFIED_MODE ? "Switch Sides" : "Human vs MainBot", ref buttonPos, spacing, buttonSize))
            {
                var whiteType = controller.HumanWasWhiteLastGame ? ChallengeController.PlayerType.MyBot : ChallengeController.PlayerType.Human;
                var blackType = !controller.HumanWasWhiteLastGame ? ChallengeController.PlayerType.MyBot : ChallengeController.PlayerType.Human;
                controller.StartNewGame(whiteType, blackType);
            }
            
            if (!Program.SIMPLIFIED_MODE)
            {
                if (NextButtonInRow("MainBot vs MainBot", ref buttonPos, spacing, buttonSize))
                {
                    controller.StartNewBotMatch(ChallengeController.PlayerType.MyBot, ChallengeController.PlayerType.MyBot);
                }
                if (NextButtonInRow("MainBot vs TestBot", ref buttonPos, spacing, buttonSize))
                {
                    controller.StartNewBotMatch(ChallengeController.PlayerType.MyBot, ChallengeController.PlayerType.EvilBot);
                }

                // Page buttons
                buttonPos.Y += breakSpacing;

                if (NextButtonInRow("Save Games", ref buttonPos, spacing, buttonSize))
                {
                    string pgns = controller.AllPGNs;
                    string directoryPath = Path.Combine(FileHelper.AppDataPath, "Games");
                    Directory.CreateDirectory(directoryPath);
                    string fileName = FileHelper.GetUniqueFileName(directoryPath, "games", ".txt");
                    string fullPath = Path.Combine(directoryPath, fileName);
                    File.WriteAllText(fullPath, pgns);
                    ConsoleHelper.Log("Saved games to " + fullPath, false, ConsoleColor.Blue);
                }
            }

            // Difficulty selection
            buttonPos.Y += breakSpacing;

            // Draw "Difficulty" label
            Vector2 labelPos = new Vector2(buttonPos.X, buttonPos.Y - UIHelper.Scale(28));
            UIHelper.DrawText("Difficulty", labelPos, UIHelper.ScaleInt(34), 1, Color.WHITE, UIHelper.AlignH.Centre);
            buttonPos.Y += 23;

            // Create difficulty buttons (vertical layout) - same size as other buttons
            Vector2 difficultyButtonSize = buttonSize;
            float difficultySpacing = difficultyButtonSize.Y * 1.1f;

            string[] difficultyNames = { "Easy", "Medium", "Hard", "Expert" };

            for (int i = 0; i < 4; i++)
            {
                Vector2 diffPos = new Vector2(buttonPos.X, buttonPos.Y + i * difficultySpacing);
                bool isSelected = (int)MainBot.CURRENT_DIFFICULTY == i;

                if (DifficultyButton(difficultyNames[i], diffPos, difficultyButtonSize, isSelected))
                {
                    MainBot.CURRENT_DIFFICULTY = (DifficultyLevel)i;
                }
            }

            buttonPos.Y += difficultySpacing * 4;

            // Settings and quit buttons
            buttonPos.Y += breakSpacing;

            string soundButtonName = Settings.EnableSounds ? "Sound: ON" : "Sound: OFF";
            if (NextButtonInRow(soundButtonName, ref buttonPos, spacing, buttonSize))
            {
                Settings.EnableSounds = !Settings.EnableSounds;
            }
            
            if (NextButtonInRow("Exit (ESC)", ref buttonPos, spacing, buttonSize))
            {
                Environment.Exit(0);
            }

            bool NextButtonInRow(string name, ref Vector2 pos, float spacingY, Vector2 size)
            {
                bool pressed = UIHelper.Button(name, pos, size);
                pos.Y += spacingY;
                return pressed;
            }
        }

        static bool DifficultyButton(string text, Vector2 centre, Vector2 size, bool isSelected)
        {
            Rectangle rec = new(centre.X - size.X / 2, centre.Y - size.Y / 2, size.X, size.Y);

            // Custom colors for difficulty buttons
            Color normalCol = isSelected ? new(67, 204, 101, 255) : new(40, 40, 40, 255); // Green if selected
            Color hoverCol = isSelected ? new(50, 180, 80, 255) : new(3, 173, 252, 255); // Darker green if selected
            Color pressCol = isSelected ? new(30, 150, 60, 255) : new(2, 119, 173, 255); // Even darker green if selected

            bool mouseOver = MouseInRect(rec);
            bool pressed = mouseOver && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT);
            bool pressedThisFrame = pressed && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT);
            Color col = mouseOver ? (pressed ? pressCol : hoverCol) : normalCol;

            Raylib.DrawRectangleRec(rec, col);
            Color textCol = Color.WHITE;
            int fontSize = UIHelper.ScaleInt(30);

            UIHelper.DrawText(text, centre, fontSize, 1, textCol, UIHelper.AlignH.Centre);

            return pressedThisFrame;
        }

        static bool MouseInRect(Rectangle rec)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            return mousePos.X >= rec.x && mousePos.Y >= rec.y && mousePos.X <= rec.x + rec.width && mousePos.Y <= rec.y + rec.height;
        }
    }
}