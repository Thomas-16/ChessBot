using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json;
using ChessChallenge.Application.Models;
using ChessChallenge.Application.Services;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application
{
    public class ChatUI
    {
        private readonly List<ChatMessage> messages = new();
        private StringBuilder inputText = new();
        private const int MaxInputLength = 200;
        private float scrollOffset = 0f;
        private bool inputBoxFocused = false;

        // Backend integration
        private readonly GameStorageService? gameStorage;
        private readonly BackendApiService? backendApi;
        private readonly AudioService? audioService;

        // Typing effect
        private float lastTypingUpdate = 0f;
        private const float TypingSpeed = 0.03f; // seconds per character

        // Timeout tracking
        private ChatMessage? pendingResponseMessage = null;
        private float pendingResponseStartTime = 0f;
        private const float ResponseTimeout = 10f; // seconds

        // Request timing
        private DateTime? lastRequestSentTime = null;

        // Layout constants (reference 1920x1080)
        private readonly Vector2 panelPos = new(1420, 150);
        private readonly Vector2 panelSize = new(450, 800);
        private readonly float headerHeight = 50f;
        private readonly float inputHeight = 60f;
        private readonly float messagePadding = 10f;
        private readonly float bubblePadding = 12f;

        public ChatUI(GameStorageService? gameStorage = null, BackendApiService? backendApi = null, AudioService? audioService = null)
        {
            this.gameStorage = gameStorage;
            this.backendApi = backendApi;
            this.audioService = audioService;

            // Register backend handlers if available
            if (backendApi != null)
            {
                backendApi.RegisterChatResponseHandler(OnReceiveChatResponse);
                backendApi.RegisterChatErrorHandler(OnChatError);
                backendApi.RegisterAudioChunkHandler(OnReceiveAudioChunk);
            }

            // Add a welcome message from AI
            AddMessage("AI", "Hey! Ready to play some chess?", false);
        }

        public void AddMessage(string sender, string message, bool isFromUser)
        {
            messages.Add(new ChatMessage(sender, message, isFromUser));
            // Auto-scroll to bottom
            scrollOffset = 0f;
        }

        public void ClearMessages()
        {
            messages.Clear();
            inputText.Clear();
        }

        public void Update()
        {
            HandleInputBoxClick();
            HandleKeyboardInput();
            UpdateTypingEffect();
            CheckResponseTimeout();

            // Update streaming audio (feeds queued chunks to AudioStream)
            if (audioService != null)
            {
                audioService.UpdateStreamingAudio();
            }
        }

        private void UpdateTypingEffect()
        {
            float currentTime = (float)Raylib.GetTime();

            foreach (ChatMessage message in messages)
            {
                if (message.IsTyping && !message.IsFromUser)
                {
                    if (currentTime - lastTypingUpdate >= TypingSpeed)
                    {
                        message.VisibleCharCount++;
                        lastTypingUpdate = currentTime;

                        // Stop typing when all characters are visible
                        if (message.VisibleCharCount >= message.Message.Length)
                        {
                            message.IsTyping = false;
                        }
                    }
                }
            }
        }

        private void CheckResponseTimeout()
        {
            if (pendingResponseMessage != null)
            {
                float currentTime = (float)Raylib.GetTime();
                if (currentTime - pendingResponseStartTime >= ResponseTimeout)
                {
                    Log("⏱️ Chat response timeout - no response from backend", false, ConsoleColor.Yellow);

                    // Replace placeholder with error message
                    if (messages.Contains(pendingResponseMessage))
                    {
                        pendingResponseMessage.Message = "Error responding right now.";
                        pendingResponseMessage.IsPlaceholder = false;
                        pendingResponseMessage.IsTyping = false;
                    }

                    pendingResponseMessage = null;
                }
            }
        }

        public void Draw()
        {
            DrawChatPanel();
            DrawChatHeader();
            DrawMessages();
            DrawInputBox();
        }

        private void DrawChatPanel()
        {
            Vector2 scaledPos = UIHelper.Scale(panelPos);
            Vector2 scaledSize = UIHelper.Scale(panelSize);

            // Semi-transparent dark background
            Rectangle panelRect = new Rectangle(scaledPos.X, scaledPos.Y, scaledSize.X, scaledSize.Y);
            Raylib.DrawRectangleRec(panelRect, new Color(30, 30, 30, 200));

            // Border
            Raylib.DrawRectangleLinesEx(panelRect, 2, new Color(60, 60, 60, 255));
        }

        private void DrawChatHeader()
        {
            Vector2 scaledPos = UIHelper.Scale(panelPos);
            Vector2 scaledSize = UIHelper.Scale(panelSize);
            float scaledHeaderHeight = UIHelper.Scale(headerHeight);

            // Header background
            Rectangle headerRect = new Rectangle(scaledPos.X, scaledPos.Y, scaledSize.X, scaledHeaderHeight);
            Raylib.DrawRectangleRec(headerRect, new Color(40, 40, 40, 255));

            // Header text
            Vector2 headerCenter = new Vector2(scaledPos.X + scaledSize.X / 2, scaledPos.Y + scaledHeaderHeight / 2);
            int headerFontSize = UIHelper.ScaleInt(32);
            UIHelper.DrawText("Chat with AI Bot", headerCenter, headerFontSize, 1, Color.WHITE, UIHelper.AlignH.Centre, UIHelper.AlignV.Centre);

            // Header bottom border
            Raylib.DrawLineEx(
                new Vector2(scaledPos.X, scaledPos.Y + scaledHeaderHeight),
                new Vector2(scaledPos.X + scaledSize.X, scaledPos.Y + scaledHeaderHeight),
                2, new Color(60, 60, 60, 255)
            );
        }

        private void DrawMessages()
        {
            Vector2 scaledPos = UIHelper.Scale(panelPos);
            Vector2 scaledSize = UIHelper.Scale(panelSize);
            float scaledHeaderHeight = UIHelper.Scale(headerHeight);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            // Message area bounds
            float messageAreaY = scaledPos.Y + scaledHeaderHeight + scaledPadding;
            float messageAreaHeight = scaledSize.Y - scaledHeaderHeight - scaledInputHeight - scaledPadding * 2;
            float messageAreaX = scaledPos.X + scaledPadding;
            float messageAreaWidth = scaledSize.X - scaledPadding * 2;

            // Enable scissor mode for clipping
            Raylib.BeginScissorMode(
                (int)messageAreaX,
                (int)messageAreaY,
                (int)messageAreaWidth,
                (int)messageAreaHeight
            );

            float currentY = messageAreaY + scrollOffset;

            foreach (ChatMessage message in messages)
            {
                currentY = DrawMessageBubble(message, messageAreaX, currentY, messageAreaWidth);
                currentY += scaledPadding;
            }

            Raylib.EndScissorMode();
        }

        private float DrawMessageBubble(ChatMessage message, float areaX, float startY, float areaWidth)
        {
            int fontSize = UIHelper.ScaleInt(24);
            int senderFontSize = UIHelper.ScaleInt(18);
            float scaledBubblePadding = UIHelper.Scale(bubblePadding);

            // Get visible message (with typing effect)
            string displayMessage = message.GetVisibleMessage();

            // Measure text to determine bubble size
            float maxBubbleWidth = areaWidth * 0.7f;
            string wrappedText = WrapText(displayMessage, fontSize, maxBubbleWidth - scaledBubblePadding * 2);
            Vector2 textSize = Raylib.MeasureTextEx(UIHelper.GetFont(), wrappedText, fontSize, 1);

            float bubbleWidth = textSize.X + scaledBubblePadding * 2;
            float bubbleHeight = textSize.Y + scaledBubblePadding * 2 + senderFontSize + UIHelper.Scale(4);

            // Position bubble (AI left, User right)
            float bubbleX = message.IsFromUser
                ? areaX + areaWidth - bubbleWidth
                : areaX;

            // Draw sender name
            Vector2 senderPos = new Vector2(bubbleX, startY);
            Color senderColor = message.IsFromUser ? new Color(100, 150, 255, 255) : new Color(150, 150, 150, 255);
            string sender = message.IsFromUser ? "You" : "AI Bot";
            UIHelper.DrawText(sender, senderPos, senderFontSize, 1, senderColor, UIHelper.AlignH.Left, UIHelper.AlignV.Top);

            float bubbleY = startY + senderFontSize + UIHelper.Scale(4);

            // Draw bubble background
            Rectangle bubbleRect = new Rectangle(bubbleX, bubbleY, bubbleWidth, bubbleHeight - senderFontSize - UIHelper.Scale(4));
            Color bubbleColor = message.IsFromUser
                ? new Color(50, 100, 200, 255)
                : new Color(60, 60, 60, 255);
            Raylib.DrawRectangleRounded(bubbleRect, 0.2f, 8, bubbleColor);

            // Draw message text
            Vector2 textPos = new Vector2(bubbleX + scaledBubblePadding, bubbleY + scaledBubblePadding);
            UIHelper.DrawText(wrappedText, textPos, fontSize, 1, Color.WHITE, UIHelper.AlignH.Left, UIHelper.AlignV.Top);

            return startY + bubbleHeight;
        }

        private void DrawInputBox()
        {
            Vector2 scaledPos = UIHelper.Scale(panelPos);
            Vector2 scaledSize = UIHelper.Scale(panelSize);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            float inputY = scaledPos.Y + scaledSize.Y - scaledInputHeight - scaledPadding;
            float inputX = scaledPos.X + scaledPadding;
            float inputWidth = scaledSize.X - scaledPadding * 2 - UIHelper.Scale(70); // Leave space for send button
            float sendButtonWidth = UIHelper.Scale(60);
            float sendButtonX = inputX + inputWidth + scaledPadding;

            // Input box background
            Rectangle inputRect = new Rectangle(inputX, inputY, inputWidth, scaledInputHeight);
            Color inputBgColor = inputBoxFocused
                ? new Color(50, 50, 50, 255)
                : new Color(40, 40, 40, 255);
            Raylib.DrawRectangleRounded(inputRect, 0.2f, 8, inputBgColor);
            Raylib.DrawRectangleRoundedLines(inputRect, 0.2f, 8, 2, new Color(80, 80, 80, 255));

            // Input text or placeholder
            int fontSize = UIHelper.ScaleInt(24);
            Vector2 textPos = new Vector2(inputX + UIHelper.Scale(12), inputY + scaledInputHeight / 2);
            string displayText = inputText.Length > 0 ? inputText.ToString() : "Type message...";
            Color textColor = inputText.Length > 0 ? Color.WHITE : new Color(120, 120, 120, 255);
            UIHelper.DrawText(displayText, textPos, fontSize, 1, textColor, UIHelper.AlignH.Left, UIHelper.AlignV.Centre);

            // Cursor blink when focused
            if (inputBoxFocused && (DateTime.Now.Millisecond / 500) % 2 == 0)
            {
                Vector2 cursorTextSize = Raylib.MeasureTextEx(UIHelper.GetFont(), inputText.ToString(), fontSize, 1);
                float cursorX = textPos.X + cursorTextSize.X;
                Raylib.DrawLineEx(
                    new Vector2(cursorX, inputY + UIHelper.Scale(15)),
                    new Vector2(cursorX, inputY + scaledInputHeight - UIHelper.Scale(15)),
                    2, Color.WHITE
                );
            }

            // Send button
            DrawSendButton(sendButtonX, inputY, sendButtonWidth, scaledInputHeight);
        }

        private void DrawSendButton(float x, float y, float width, float height)
        {
            Rectangle buttonRect = new Rectangle(x, y, width, height);
            bool mouseOver = MouseInRect(buttonRect);
            bool canSend = inputText.Length > 0;

            Color buttonColor;
            if (!canSend)
            {
                buttonColor = new Color(60, 60, 60, 255);
            }
            else if (mouseOver && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
            {
                buttonColor = new Color(40, 120, 220, 255);
            }
            else if (mouseOver)
            {
                buttonColor = new Color(60, 140, 240, 255);
            }
            else
            {
                buttonColor = new Color(50, 100, 200, 255);
            }

            Raylib.DrawRectangleRounded(buttonRect, 0.2f, 8, buttonColor);

            // Send icon (triangle/arrow)
            Vector2 center = new Vector2(x + width / 2, y + height / 2);
            int fontSize = UIHelper.ScaleInt(28);
            UIHelper.DrawText(">", center, fontSize, 1, Color.WHITE, UIHelper.AlignH.Centre, UIHelper.AlignV.Centre);

            // Handle click
            if (canSend && mouseOver && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
            {
                SendMessage();
            }
        }

        private void HandleInputBoxClick()
        {
            Vector2 scaledPos = UIHelper.Scale(panelPos);
            Vector2 scaledSize = UIHelper.Scale(panelSize);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            float inputY = scaledPos.Y + scaledSize.Y - scaledInputHeight - scaledPadding;
            float inputX = scaledPos.X + scaledPadding;
            float inputWidth = scaledSize.X - scaledPadding * 2 - UIHelper.Scale(70);

            Rectangle inputRect = new Rectangle(inputX, inputY, inputWidth, scaledInputHeight);

            if (Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
            {
                inputBoxFocused = MouseInRect(inputRect);
            }
        }

        private void HandleKeyboardInput()
        {
            if (!inputBoxFocused) return;

            // Handle character input
            int key = Raylib.GetCharPressed();
            while (key > 0)
            {
                if (key >= 32 && key <= 126 && inputText.Length < MaxInputLength)
                {
                    inputText.Append((char)key);
                }
                key = Raylib.GetCharPressed();
            }

            // Handle backspace
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_BACKSPACE) && inputText.Length > 0)
            {
                inputText.Remove(inputText.Length - 1, 1);
            }

            // Handle enter key
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER) && inputText.Length > 0)
            {
                SendMessage();
            }
        }

        private async void SendMessage()
        {
            string messageText = inputText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(messageText)) return;

            // Add user message
            AddMessage("User", messageText, true);

            // Clear input
            inputText.Clear();

            // Send to backend if available
            if (backendApi != null && gameStorage != null)
            {
                string? gameId = gameStorage.GetCurrentGameId();

                if (string.IsNullOrEmpty(gameId))
                {
                    Log("⚠️ No active game session - cannot send chat message", false, ConsoleColor.Yellow);
                    AddMessage("System", "Cannot chat - no active game session", false);
                    return;
                }

                // Add placeholder "..." message for AI
                ChatMessage placeholderMessage = new ChatMessage("AI", "...", false, isPlaceholder: true);
                messages.Add(placeholderMessage);
                scrollOffset = 0f;

                // Start timeout tracking and timing
                pendingResponseMessage = placeholderMessage;
                pendingResponseStartTime = (float)Raylib.GetTime();
                lastRequestSentTime = DateTime.UtcNow;

                // Send message to backend
                bool success = await backendApi.SendChatMessageAsync(gameId, messageText);

                if (!success)
                {
                    // Remove placeholder on failure
                    messages.Remove(placeholderMessage);
                    pendingResponseMessage = null;
                    AddMessage("System", "Failed to send message - backend not connected", false);
                }
            }
            else
            {
                Log($"[Chat] User sent: {messageText} (backend not configured)", false, ConsoleColor.Cyan);
            }
        }

        private void OnReceiveChatResponse(object response)
        {
            try
            {
                // Clear timeout tracking since we got a response
                pendingResponseMessage = null;

                // Calculate response time
                double responseTimeMs = 0;
                if (lastRequestSentTime.HasValue)
                {
                    responseTimeMs = (DateTime.UtcNow - lastRequestSentTime.Value).TotalMilliseconds;
                    lastRequestSentTime = null;
                }

                // Parse response JSON
                JsonElement jsonElement = (JsonElement)response;
                string message = jsonElement.GetProperty("message").GetString() ?? "No response";

                Log($"💬 Received AI response ({responseTimeMs:F0}ms): {message}", false, ConsoleColor.Green);

                // Find and replace placeholder message
                int placeholderIndex = messages.FindIndex(m => m.IsPlaceholder);
                if (placeholderIndex >= 0)
                {
                    messages.RemoveAt(placeholderIndex);
                }

                // Add AI response with typing effect
                ChatMessage aiMessage = new ChatMessage("AI", message, false)
                {
                    IsTyping = true,
                    VisibleCharCount = 0
                };
                messages.Add(aiMessage);
                lastTypingUpdate = (float)Raylib.GetTime();

                // Start streaming audio session (audio chunks will arrive separately)
                if (audioService != null)
                {
                    audioService.StartStreamingAudio();
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error processing chat response: {ex.Message}", false, ConsoleColor.Red);
                // Clear timeout tracking
                pendingResponseMessage = null;
                // Remove placeholder on error
                int placeholderIndex = messages.FindIndex(m => m.IsPlaceholder);
                if (placeholderIndex >= 0)
                {
                    messages.RemoveAt(placeholderIndex);
                }
                AddMessage("System", "Error receiving AI response", false);
            }
        }

        private void OnChatError(string error)
        {
            Log($"❌ Chat error: {error}", false, ConsoleColor.Red);

            // Clear timeout tracking
            pendingResponseMessage = null;

            // Remove placeholder on error
            int placeholderIndex = messages.FindIndex(m => m.IsPlaceholder);
            if (placeholderIndex >= 0)
            {
                messages.RemoveAt(placeholderIndex);
            }

            AddMessage("System", $"Error: {error}", false);
        }

        private void OnReceiveAudioChunk(object chunkData)
        {
            try
            {
                // Parse chunk JSON
                JsonElement jsonElement = (JsonElement)chunkData;
                string audioChunkBase64 = jsonElement.GetProperty("audioChunk").GetString() ?? "";
                bool isLast = jsonElement.GetProperty("isLast").GetBoolean();

                if (audioService == null)
                {
                    return;
                }

                if (isLast)
                {
                    // Finish streaming
                    audioService.FinishStreamingAudio();
                    Log("🔊 Audio streaming complete", false, ConsoleColor.Green);
                }
                else if (!string.IsNullOrEmpty(audioChunkBase64))
                {
                    // Decode and add chunk
                    byte[] chunkBytes = Convert.FromBase64String(audioChunkBase64);
                    audioService.AddAudioChunk(chunkBytes);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error processing audio chunk: {ex.Message}", false, ConsoleColor.Red);
            }
        }

        private string WrapText(string text, int fontSize, float maxWidth)
        {
            string[] words = text.Split(' ');
            List<string> lines = new List<string>();
            StringBuilder currentLine = new StringBuilder();

            foreach (string word in words)
            {
                string testLine = currentLine.Length > 0 ? currentLine + " " + word : word;
                Vector2 size = Raylib.MeasureTextEx(UIHelper.GetFont(), testLine, fontSize, 1);

                if (size.X > maxWidth && currentLine.Length > 0)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(word);
                }
                else
                {
                    if (currentLine.Length > 0) currentLine.Append(" ");
                    currentLine.Append(word);
                }
            }

            if (currentLine.Length > 0)
            {
                lines.Add(currentLine.ToString());
            }

            return string.Join("\n", lines);
        }

        private bool MouseInRect(Rectangle rec)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            return mousePos.X >= rec.x && mousePos.Y >= rec.y &&
                   mousePos.X <= rec.x + rec.width && mousePos.Y <= rec.y + rec.height;
        }
    }
}