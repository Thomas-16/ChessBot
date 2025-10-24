using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
        private float inputTextOffset = 0f; // Horizontal scroll offset for input text

        // Backend integration
        private readonly GameStorageService? gameStorage;
        private readonly BackendApiService? backendApi;
        private readonly AudioService? audioService;
        private readonly Func<ChessPositionContext?>? contextExtractor;

        // Typing effect
        private float lastTypingUpdate = 0f;
        private const float TypingSpeed = 0.03f; // seconds per character

        // Timeout tracking
        private ChatMessage? pendingResponseMessage = null;
        private float pendingResponseStartTime = 0f;
        private const float ResponseTimeout = 10f; // seconds

        // Request timing
        private DateTime? lastRequestSentTime = null;

        // Automatic message tracking
        private bool isAutomaticMessagePending = false;

        // UI animation states
        private float inputBoxFocusLerp = 0f;
        private float sendButtonHoverLerp = 0f;
        private float sendButtonPressLerp = 0f;
        private float scrollbarAlpha = 0f;
        private float micButtonHoverLerp = 0f;
        private float micButtonPressLerp = 0f;
        private float micButtonPulseLerp = 0f;

        // Layout constants (reference 1920x1080)
        private readonly float panelWidthRef = 450f;
        private readonly float panelHeightRef = 800f;
        private readonly float panelRightMarginRef = 50f; // Distance from right edge
        private readonly float panelTopMarginRef = 150f; // Distance from top
        private readonly float headerHeight = 50f;
        private readonly float inputHeight = 60f;
        private readonly float messagePadding = 10f;
        private readonly float bubblePadding = 12f;

        // Button icons
        private Texture2D micIcon;
        private Texture2D sendIcon;

        public ChatUI(GameStorageService? gameStorage = null, BackendApiService? backendApi = null, AudioService? audioService = null, Func<ChessPositionContext?>? contextExtractor = null)
        {
            this.gameStorage = gameStorage;
            this.backendApi = backendApi;
            this.audioService = audioService;
            this.contextExtractor = contextExtractor;

            // Load button icons
            micIcon = Raylib.LoadTexture("resources/Icons/mic.png");
            sendIcon = Raylib.LoadTexture("resources/Icons/send.png");

            // Register backend handlers if available
            if (backendApi != null)
            {
                backendApi.RegisterChatResponseHandler(OnReceiveChatResponse);
                backendApi.RegisterChatErrorHandler(OnChatError);
            }

            // Add a welcome message from AI
            AddMessage("AI", "Hey! Ready to play some chess?", false);
        }

        public void AddMessage(string sender, string message, bool isFromUser)
        {
            messages.Add(new ChatMessage(sender, message, isFromUser));
            // Auto-scroll to bottom
            ScrollToBottom();
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
            HandleScrollInput();
            UpdateTypingEffect();
            CheckResponseTimeout();
            UpdateAnimations();
        }

        private void UpdateAnimations()
        {
            float deltaTime = Raylib.GetFrameTime();
            float lerpSpeed = 8f;

            // Input box focus animation
            float targetFocus = inputBoxFocused ? 1f : 0f;
            inputBoxFocusLerp = Lerp(inputBoxFocusLerp, targetFocus, deltaTime * lerpSpeed);

            // Microphone button pulse animation (when recording)
            if (audioService?.IsRecording == true)
            {
                micButtonPulseLerp += deltaTime * 3f;
                if (micButtonPulseLerp > 1f) micButtonPulseLerp = 0f;
            }
            else
            {
                micButtonPulseLerp = 0f;
            }

            // Scrollbar fade animation
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
            float scaledHeaderHeight = UIHelper.Scale(headerHeight);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            float messageAreaX = scaledPos.X;
            float messageAreaY = scaledPos.Y + scaledHeaderHeight;
            float messageAreaWidth = scaledSize.X;
            float messageAreaHeight = scaledSize.Y - scaledHeaderHeight - scaledInputHeight - scaledPadding;

            Rectangle messageAreaRect = new Rectangle(messageAreaX, messageAreaY, messageAreaWidth, messageAreaHeight);
            Vector2 mousePos = Raylib.GetMousePosition();
            bool mouseOverMessages = MouseInRect(messageAreaRect);

            float targetScrollbarAlpha = mouseOverMessages ? 1f : 0f;
            scrollbarAlpha = Lerp(scrollbarAlpha, targetScrollbarAlpha, deltaTime * 6f);
        }

        private float Lerp(float a, float b, float t)
        {
            t = Math.Min(1, Math.Max(t, 0));
            return a + (b - a) * t;
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
                        bool wasAtBottom = IsScrolledToBottom();

                        message.VisibleCharCount++;
                        lastTypingUpdate = currentTime;

                        // Auto-scroll to bottom if we were at the bottom before the character was added
                        if (wasAtBottom)
                        {
                            ScrollToBottom();
                        }

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

        private Vector2 GetPanelPosition()
        {
            // Calculate panel position based on actual screen size
            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();

            float scaledWidth = UIHelper.Scale(panelWidthRef);
            float scaledHeight = UIHelper.Scale(panelHeightRef);
            float scaledRightMargin = UIHelper.Scale(panelRightMarginRef);
            float scaledTopMargin = UIHelper.Scale(panelTopMarginRef);

            float posX = screenWidth - scaledWidth - scaledRightMargin;
            float posY = scaledTopMargin;

            return new Vector2(posX, posY);
        }

        private Vector2 GetPanelSize()
        {
            return new Vector2(UIHelper.Scale(panelWidthRef), UIHelper.Scale(panelHeightRef));
        }

        private void DrawChatPanel()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();

            // Semi-transparent dark background with subtle gradient effect
            Rectangle panelRect = new Rectangle(scaledPos.X, scaledPos.Y, scaledSize.X, scaledSize.Y);
            Raylib.DrawRectangleRec(panelRect, new Color(30, 30, 30, 220));

            // Subtle inner shadow effect
            Rectangle innerShadow = new Rectangle(scaledPos.X + 2, scaledPos.Y + 2, scaledSize.X - 4, scaledSize.Y - 4);
            Raylib.DrawRectangleLinesEx(innerShadow, 1, new Color(10, 10, 10, 60));

            // Border with slight highlight
            Raylib.DrawRectangleLinesEx(panelRect, 2, new Color(70, 70, 70, 255));
        }

        private void DrawChatHeader()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
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
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
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

            // Draw scrollbar if content overflows
            DrawScrollbar(messageAreaX, messageAreaY, messageAreaWidth, messageAreaHeight);
        }

        private void DrawScrollbar(float areaX, float areaY, float areaWidth, float areaHeight)
        {
            if (scrollbarAlpha < 0.01f) return;

            float totalContentHeight = CalculateTotalContentHeight();
            if (totalContentHeight <= areaHeight) return;

            float scrollbarWidth = UIHelper.Scale(4f);
            float scrollbarX = areaX + areaWidth - scrollbarWidth - UIHelper.Scale(4f);

            // Calculate scrollbar height and position
            float visibleRatio = areaHeight / totalContentHeight;
            float scrollbarHeight = Math.Max(areaHeight * visibleRatio, UIHelper.Scale(30f));

            float maxScroll = -(totalContentHeight - areaHeight);
            float scrollProgress = maxScroll == 0 ? 0 : Math.Abs(scrollOffset / maxScroll);
            float scrollbarY = areaY + scrollProgress * (areaHeight - scrollbarHeight);

            // Draw scrollbar track (subtle)
            Rectangle trackRect = new Rectangle(scrollbarX, areaY, scrollbarWidth, areaHeight);
            Raylib.DrawRectangleRounded(trackRect, 1f, 8, new Color(60, 60, 60, (int)(80 * scrollbarAlpha)));

            // Draw scrollbar thumb
            Rectangle thumbRect = new Rectangle(scrollbarX, scrollbarY, scrollbarWidth, scrollbarHeight);
            Raylib.DrawRectangleRounded(thumbRect, 1f, 8, new Color(120, 120, 120, (int)(200 * scrollbarAlpha)));
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

            // Draw subtle shadow behind bubble
            Rectangle shadowRect = new Rectangle(bubbleX + 2, bubbleY + 2, bubbleWidth, bubbleHeight - senderFontSize - UIHelper.Scale(4));
            Raylib.DrawRectangleRounded(shadowRect, 0.25f, 8, new Color(0, 0, 0, 40));

            // Draw bubble background with improved colors
            Rectangle bubbleRect = new Rectangle(bubbleX, bubbleY, bubbleWidth, bubbleHeight - senderFontSize - UIHelper.Scale(4));
            Color bubbleColor = message.IsFromUser
                ? new Color(55, 110, 210, 255)
                : new Color(65, 65, 65, 255);
            Raylib.DrawRectangleRounded(bubbleRect, 0.25f, 8, bubbleColor);

            // Subtle highlight on top edge
            Color highlightColor = message.IsFromUser
                ? new Color(80, 140, 230, 60)
                : new Color(85, 85, 85, 60);
            Rectangle highlightRect = new Rectangle(bubbleX, bubbleY, bubbleWidth, UIHelper.Scale(2));
            Raylib.DrawRectangleRounded(highlightRect, 0.25f, 8, highlightColor);

            // Draw message text
            Vector2 textPos = new Vector2(bubbleX + scaledBubblePadding, bubbleY + scaledBubblePadding);
            UIHelper.DrawText(wrappedText, textPos, fontSize, 1, Color.WHITE, UIHelper.AlignH.Left, UIHelper.AlignV.Top);

            return startY + bubbleHeight;
        }

        private void DrawInputBox()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            float inputY = scaledPos.Y + scaledSize.Y - scaledInputHeight - scaledPadding;
            float inputX = scaledPos.X + scaledPadding;
            float micButtonWidth = UIHelper.Scale(60);
            float sendButtonWidth = UIHelper.Scale(60);
            float inputWidth = scaledSize.X - scaledPadding * 4 - micButtonWidth - sendButtonWidth; // Leave space for mic and send buttons + right padding
            float micButtonX = inputX + inputWidth + scaledPadding;
            float sendButtonX = micButtonX + micButtonWidth + scaledPadding;

            // Input box background with smooth focus transition
            Rectangle inputRect = new Rectangle(inputX, inputY, inputWidth, scaledInputHeight);
            Color inputBgColor = new Color(
                (int)(40 + 15 * inputBoxFocusLerp),
                (int)(40 + 15 * inputBoxFocusLerp),
                (int)(40 + 15 * inputBoxFocusLerp),
                255
            );
            Raylib.DrawRectangleRounded(inputRect, 0.3f, 8, inputBgColor);

            // Animated border
            Color borderColor = new Color(
                (int)(80 + 80 * inputBoxFocusLerp),
                (int)(80 + 80 * inputBoxFocusLerp),
                (int)(80 + 80 * inputBoxFocusLerp),
                255
            );
            Raylib.DrawRectangleRoundedLines(inputRect, 0.3f, 8, 2, borderColor);

            // Calculate text scrolling for long input
            int fontSize = UIHelper.ScaleInt(24);
            float leftPadding = UIHelper.Scale(12);
            float rightPadding = UIHelper.Scale(12);
            float availableWidth = inputWidth - leftPadding - rightPadding;

            string displayText = inputText.Length > 0 ? inputText.ToString() : "Type message...";
            Color textColor = inputText.Length > 0 ? Color.WHITE : new Color(120, 120, 120, 255);

            // Measure full text width
            Vector2 fullTextSize = Raylib.MeasureTextEx(UIHelper.GetFont(), displayText, fontSize, 1);

            // Auto-scroll input text to keep cursor visible
            if (inputText.Length > 0 && fullTextSize.X > availableWidth)
            {
                // Keep the end of the text visible (where cursor is)
                inputTextOffset = -(fullTextSize.X - availableWidth);
            }
            else
            {
                inputTextOffset = 0f;
            }

            // Enable scissor mode for text clipping
            Raylib.BeginScissorMode(
                (int)(inputX + leftPadding),
                (int)inputY,
                (int)availableWidth,
                (int)scaledInputHeight
            );

            Vector2 textPos = new Vector2(inputX + leftPadding + inputTextOffset, inputY + scaledInputHeight / 2);
            UIHelper.DrawText(displayText, textPos, fontSize, 1, textColor, UIHelper.AlignH.Left, UIHelper.AlignV.Centre);

            // Cursor blink when focused
            if (inputBoxFocused && (DateTime.Now.Millisecond / 500) % 2 == 0 && inputText.Length > 0)
            {
                Vector2 cursorTextSize = Raylib.MeasureTextEx(UIHelper.GetFont(), inputText.ToString(), fontSize, 1);
                float cursorX = textPos.X + cursorTextSize.X;
                Raylib.DrawLineEx(
                    new Vector2(cursorX, inputY + UIHelper.Scale(15)),
                    new Vector2(cursorX, inputY + scaledInputHeight - UIHelper.Scale(15)),
                    2, Color.WHITE
                );
            }

            Raylib.EndScissorMode();

            // Microphone button
            DrawMicButton(micButtonX, inputY, micButtonWidth, scaledInputHeight);

            // Send button
            DrawSendButton(sendButtonX, inputY, sendButtonWidth, scaledInputHeight);
        }

        private void DrawMicButton(float x, float y, float width, float height)
        {
            Rectangle buttonRect = new Rectangle(x, y, width, height);
            bool mouseOver = MouseInRect(buttonRect);
            bool isRecording = audioService?.IsRecording == true;

            // Check if transcription service is ready
            GroqWhisperService? groqWhisper = Program.ServiceProvider?.GetService<GroqWhisperService>();

            bool canUse = pendingResponseMessage == null && !isAutomaticMessagePending && audioService != null && groqWhisper?.IsInitialized == true; // Disable if waiting for response, automatic message pending, no audio service, or no transcription available
            bool isPressed = mouseOver && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT);

            // Smooth hover/press animations
            float deltaTime = Raylib.GetFrameTime();
            float targetHover = (mouseOver && canUse) ? 1f : 0f;
            float targetPress = isPressed ? 1f : 0f;
            micButtonHoverLerp = Lerp(micButtonHoverLerp, targetHover, deltaTime * 12f);
            micButtonPressLerp = Lerp(micButtonPressLerp, targetPress, deltaTime * 20f);

            // Scale effect on press
            float scaleEffect = 1f - micButtonPressLerp * 0.08f;
            float scaledWidth = width * scaleEffect;
            float scaledHeight = height * scaleEffect;
            float offsetX = (width - scaledWidth) / 2f;
            float offsetY = (height - scaledHeight) / 2f;

            Rectangle scaledButtonRect = new Rectangle(x + offsetX, y + offsetY, scaledWidth, scaledHeight);

            // Button color with smooth transitions
            Color buttonColor;
            if (!canUse)
            {
                buttonColor = new Color(60, 60, 60, 255);
            }
            else if (isRecording)
            {
                // Pulsing red when recording
                float pulse = MathF.Sin(micButtonPulseLerp * MathF.PI * 2f) * 0.5f + 0.5f;
                int r = (int)(180 + 75 * pulse);
                int g = (int)(20 + 10 * pulse);
                int b = (int)(20 + 10 * pulse);
                buttonColor = new Color(r, g, b, 255);
            }
            else
            {
                int r = (int)(50 + 10 * micButtonHoverLerp - 10 * micButtonPressLerp);
                int g = (int)(100 + 40 * micButtonHoverLerp - 20 * micButtonPressLerp);
                int b = (int)(200 + 40 * micButtonHoverLerp - 20 * micButtonPressLerp);
                buttonColor = new Color(r, g, b, 255);
            }

            Raylib.DrawRectangleRounded(scaledButtonRect, 0.3f, 8, buttonColor);

            // Glow effect when hovering or recording
            if ((canUse && micButtonHoverLerp > 0.1f) || isRecording)
            {
                float glowAlpha = isRecording ? 60f : 40f * micButtonHoverLerp;
                Color glowColor = isRecording
                    ? new Color(255, 100, 100, (int)glowAlpha)
                    : new Color(100, 180, 255, (int)glowAlpha);
                Rectangle glowRect = new Rectangle(x + offsetX - 2, y + offsetY - 2, scaledWidth + 4, scaledHeight + 4);
                Raylib.DrawRectangleRounded(glowRect, 0.3f, 8, glowColor);
                Raylib.DrawRectangleRounded(scaledButtonRect, 0.3f, 8, buttonColor);
            }

            // Volume indicator (red fill behind mic when recording)
            if (isRecording && audioService != null)
            {
                float volume = audioService.CurrentVolume; // 0.0 to 1.0
                float iconSize = Math.Min(scaledWidth, scaledHeight) * 0.5f;
                float indicatorHeight = iconSize * volume; // Volume-based height

                // Draw red fill from bottom up
                float indicatorY = y + offsetY + (scaledHeight - indicatorHeight) / 2f + (iconSize - indicatorHeight);
                Rectangle volumeRect = new Rectangle(
                    x + offsetX + (scaledWidth - iconSize) / 2f,
                    indicatorY,
                    iconSize,
                    indicatorHeight
                );

                // Pulsing red color based on volume
                int redIntensity = (int)(200 + 55 * volume);
                Color volumeColor = new Color(redIntensity, 20, 20, 180);
                Raylib.DrawRectangleRec(volumeRect, volumeColor);
            }

            // Mic icon
            Vector2 center = new Vector2(x + offsetX + scaledWidth / 2, y + offsetY + scaledHeight / 2);
            float micIconSize = Math.Min(scaledWidth, scaledHeight) * 0.5f;

            Color iconTint = canUse ? Color.WHITE : new Color(120, 120, 120, 255);

            // Draw mic icon texture
            Rectangle sourceRect = new Rectangle(0, 0, micIcon.width, micIcon.height);
            Rectangle destRect = new Rectangle(
                center.X - micIconSize / 2,
                center.Y - micIconSize / 2,
                micIconSize,
                micIconSize
            );
            Raylib.DrawTexturePro(micIcon, sourceRect, destRect, new Vector2(0, 0), 0f, iconTint);

            // Recording duration text
            if (isRecording && audioService != null)
            {
                double duration = audioService.RecordingDuration;
                string durationText = $"{duration:F1}s";
                Vector2 durationPos = new Vector2(x + width / 2, y - UIHelper.Scale(15));
                int durationFontSize = UIHelper.ScaleInt(16);
                UIHelper.DrawText(durationText, durationPos, durationFontSize, 1, new Color(255, 100, 100, 255), UIHelper.AlignH.Centre, UIHelper.AlignV.Centre);
            }

            // Handle click
            if (canUse && mouseOver && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
            {
                HandleMicButtonClick();
            }
        }

        private void DrawSendButton(float x, float y, float width, float height)
        {
            Rectangle buttonRect = new Rectangle(x, y, width, height);
            bool mouseOver = MouseInRect(buttonRect);
            bool canSend = inputText.Length > 0 && pendingResponseMessage == null && !isAutomaticMessagePending; // Disable if waiting for response or automatic message pending
            bool isPressed = mouseOver && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT);

            // Smooth hover/press animations
            float deltaTime = Raylib.GetFrameTime();
            float targetHover = (mouseOver && canSend) ? 1f : 0f;
            float targetPress = isPressed ? 1f : 0f;
            sendButtonHoverLerp = Lerp(sendButtonHoverLerp, targetHover, deltaTime * 12f);
            sendButtonPressLerp = Lerp(sendButtonPressLerp, targetPress, deltaTime * 20f);

            // Scale effect on press
            float scaleEffect = 1f - sendButtonPressLerp * 0.08f;
            float scaledWidth = width * scaleEffect;
            float scaledHeight = height * scaleEffect;
            float offsetX = (width - scaledWidth) / 2f;
            float offsetY = (height - scaledHeight) / 2f;

            Rectangle scaledButtonRect = new Rectangle(x + offsetX, y + offsetY, scaledWidth, scaledHeight);

            // Button color with smooth transitions
            Color buttonColor;
            if (!canSend)
            {
                buttonColor = new Color(60, 60, 60, 255);
            }
            else
            {
                int r = (int)(50 + 10 * sendButtonHoverLerp - 10 * sendButtonPressLerp);
                int g = (int)(100 + 40 * sendButtonHoverLerp - 20 * sendButtonPressLerp);
                int b = (int)(200 + 40 * sendButtonHoverLerp - 20 * sendButtonPressLerp);
                buttonColor = new Color(r, g, b, 255);
            }

            Raylib.DrawRectangleRounded(scaledButtonRect, 0.3f, 8, buttonColor);

            // Send icon with subtle glow on hover
            if (canSend && sendButtonHoverLerp > 0.1f)
            {
                Color glowColor = new Color(100, 180, 255, (int)(40 * sendButtonHoverLerp));
                Rectangle glowRect = new Rectangle(x + offsetX - 2, y + offsetY - 2, scaledWidth + 4, scaledHeight + 4);
                Raylib.DrawRectangleRounded(glowRect, 0.3f, 8, glowColor);
                Raylib.DrawRectangleRounded(scaledButtonRect, 0.3f, 8, buttonColor);
            }

            // Send icon
            Vector2 center = new Vector2(x + offsetX + scaledWidth / 2, y + offsetY + scaledHeight / 2);
            float iconSize = Math.Min(scaledWidth, scaledHeight) * 0.5f;

            Color iconTint = canSend ? Color.WHITE : new Color(120, 120, 120, 255);

            // Draw send icon texture
            Rectangle sourceRect = new Rectangle(0, 0, sendIcon.width, sendIcon.height);
            Rectangle destRect = new Rectangle(
                center.X - iconSize / 2,
                center.Y - iconSize / 2,
                iconSize,
                iconSize
            );
            Raylib.DrawTexturePro(sendIcon, sourceRect, destRect, new Vector2(0, 0), 0f, iconTint);

            // Handle click
            if (canSend && mouseOver && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
            {
                SendMessage();
            }
        }

        private void HandleInputBoxClick()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
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

        private void HandleScrollInput()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
            float scaledHeaderHeight = UIHelper.Scale(headerHeight);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            // Message area bounds
            float messageAreaX = scaledPos.X;
            float messageAreaY = scaledPos.Y + scaledHeaderHeight;
            float messageAreaWidth = scaledSize.X;
            float messageAreaHeight = scaledSize.Y - scaledHeaderHeight - scaledInputHeight - scaledPadding;

            Rectangle messageAreaRect = new Rectangle(messageAreaX, messageAreaY, messageAreaWidth, messageAreaHeight);
            Vector2 mousePos = Raylib.GetMousePosition();

            // Only scroll if mouse is over the message area
            if (MouseInRect(messageAreaRect))
            {
                float wheelMove = Raylib.GetMouseWheelMove();
                if (wheelMove != 0)
                {
                    // Scroll speed (pixels per wheel tick)
                    float scrollSpeed = UIHelper.Scale(40f);
                    scrollOffset += wheelMove * scrollSpeed;

                    // Calculate total content height
                    float totalContentHeight = CalculateTotalContentHeight();

                    // Clamp scroll offset
                    // Max scroll is 0 (bottom), min scroll shows top of content
                    float minScroll = -(totalContentHeight - messageAreaHeight + scaledPadding);
                    if (minScroll > 0) minScroll = 0; // If content fits, don't scroll

                    scrollOffset = Math.Clamp(scrollOffset, minScroll, 0);
                }
            }
        }

        private float CalculateTotalContentHeight()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
            float scaledHeaderHeight = UIHelper.Scale(headerHeight);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            float messageAreaX = scaledPos.X + scaledPadding;
            float messageAreaWidth = scaledSize.X - scaledPadding * 2;

            float totalHeight = 0;

            foreach (ChatMessage message in messages)
            {
                float bubbleHeight = CalculateMessageHeight(message, messageAreaWidth);
                totalHeight += bubbleHeight + scaledPadding;
            }

            return totalHeight;
        }

        private float CalculateMessageHeight(ChatMessage message, float areaWidth)
        {
            int fontSize = UIHelper.ScaleInt(24);
            int senderFontSize = UIHelper.ScaleInt(18);
            float scaledBubblePadding = UIHelper.Scale(bubblePadding);

            string displayMessage = message.GetVisibleMessage();
            float maxBubbleWidth = areaWidth * 0.7f;
            string wrappedText = WrapText(displayMessage, fontSize, maxBubbleWidth - scaledBubblePadding * 2);
            Vector2 textSize = Raylib.MeasureTextEx(UIHelper.GetFont(), wrappedText, fontSize, 1);

            float bubbleHeight = textSize.Y + scaledBubblePadding * 2 + senderFontSize + UIHelper.Scale(4);
            return bubbleHeight;
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

            // Handle enter key (only if not waiting for response and no automatic message pending)
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER) && inputText.Length > 0 && pendingResponseMessage == null && !isAutomaticMessagePending)
            {
                SendMessage();
            }
        }

        private void HandleMicButtonClick()
        {
            if (audioService == null) return;

            if (audioService.IsRecording)
            {
                // Stop recording and send
                SendVoiceMessage();
            }
            else
            {
                // Start recording
                bool started = audioService.StartRecording();
                if (!started)
                {
                    AddMessage("System", "Failed to start recording - check microphone permissions", false);
                }
            }
        }

        private async void SendVoiceMessage()
        {
            if (audioService == null) return;

            // Stop recording and get WAV file path
            string? audioFilePath = audioService.StopRecording();
            if (string.IsNullOrEmpty(audioFilePath))
            {
                AddMessage("System", "Recording failed or was empty", false);
                return;
            }

            // Stop any ongoing typing effects and audio playback
            foreach (var message in messages)
            {
                if (message.IsTyping)
                {
                    message.IsTyping = false;
                    message.VisibleCharCount = message.Message.Length;
                }
            }
            audioService.StopAudio();

            try
            {
                // Show transcribing indicator
                ChatMessage transcribingMessage = new ChatMessage("System", "Transcribing...", false, isPlaceholder: true);
                messages.Add(transcribingMessage);
                ScrollToBottom();

                string? transcribedText = null;

                // Use Groq whisper
                GroqWhisperService? groqWhisper = Program.ServiceProvider?.GetService<GroqWhisperService>();
                if (groqWhisper?.IsInitialized == true)
                {
                    Log("🚀 Using Groq Whisper API for transcription", false, ConsoleColor.Cyan);
                    transcribedText = await groqWhisper.TranscribeAsync(audioFilePath);
                }

                // Remove transcribing indicator
                messages.Remove(transcribingMessage);

                // Clean up temp WAV file
                if (File.Exists(audioFilePath))
                {
                    File.Delete(audioFilePath);
                }

                if (string.IsNullOrEmpty(transcribedText))
                {
                    AddMessage("System", "Could not transcribe audio", false);
                    return;
                }

                // Add user message with transcribed text
                AddMessage("User", transcribedText, true);

                // Send transcribed text as normal chat message
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
                    ScrollToBottom();

                    // Start timeout tracking and timing
                    pendingResponseMessage = placeholderMessage;
                    pendingResponseStartTime = (float)Raylib.GetTime();
                    lastRequestSentTime = DateTime.UtcNow;

                    // Extract position context
                    ChessPositionContext? context = contextExtractor?.Invoke();

                    // Send message to backend
                    bool success = await backendApi.SendChatMessageAsync(gameId, transcribedText, context);

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
                    Log($"[Chat] Voice transcribed: {transcribedText} (backend not configured)", false, ConsoleColor.Cyan);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error during voice transcription: {ex.Message}", false, ConsoleColor.Red);
                AddMessage("System", "Error transcribing voice message", false);

                // Clean up temp file on error
                if (File.Exists(audioFilePath))
                {
                    try
                    {
                        File.Delete(audioFilePath);
                    }
                    catch { }
                }
            }
        }

        private async void SendMessage()
        {
            string messageText = inputText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(messageText)) return;

            // Stop any ongoing typing effects and audio playback
            foreach (var message in messages)
            {
                if (message.IsTyping)
                {
                    message.IsTyping = false;
                    message.VisibleCharCount = message.Message.Length; // Show full message immediately
                }
            }

            // Stop audio playback
            audioService?.StopAudio();

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
                ScrollToBottom();

                // Start timeout tracking and timing
                pendingResponseMessage = placeholderMessage;
                pendingResponseStartTime = (float)Raylib.GetTime();
                lastRequestSentTime = DateTime.UtcNow;

                // Extract position context
                ChessPositionContext? context = contextExtractor?.Invoke();

                // Send message to backend
                bool success = await backendApi.SendChatMessageAsync(gameId, messageText, context);

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

        public bool CanSendAutomaticMessage()
        {
            // Don't send automatic message if:
            // - User is typing (has text in input box)
            // - User is recording voice
            // - There's already a pending response
            // - There's already an automatic message pending
            bool userIsTyping = inputText.Length > 0;
            bool userIsRecording = audioService?.IsRecording == true;
            bool hasPendingResponse = pendingResponseMessage != null;

            return !userIsTyping && !userIsRecording && !hasPendingResponse && !isAutomaticMessagePending;
        }

        public async void SendAutomaticChatMessage(string message)
        {
            if (!CanSendAutomaticMessage())
            {
                Log($"⏭️ Skipping automatic message - user is interacting or message pending", false, ConsoleColor.Yellow);
                return;
            }

            if (backendApi == null || gameStorage == null)
            {
                Log($"⚠️ Cannot send automatic message - backend not configured", false, ConsoleColor.Yellow);
                return;
            }

            string? gameId = gameStorage.GetCurrentGameId();
            if (string.IsNullOrEmpty(gameId))
            {
                Log("⚠️ No active game session - cannot send automatic message", false, ConsoleColor.Yellow);
                return;
            }

            try
            {
                Log($"🤖 Sending automatic chat message with custom prompt", false, ConsoleColor.Cyan);

                // Mark automatic message as pending
                isAutomaticMessagePending = true;

                // Stop any ongoing typing effects and audio playback
                foreach (var msg in messages)
                {
                    if (msg.IsTyping)
                    {
                        msg.IsTyping = false;
                        msg.VisibleCharCount = msg.Message.Length;
                    }
                }
                audioService?.StopAudio();

                // Add placeholder "..." message for AI
                ChatMessage placeholderMessage = new ChatMessage("AI", "...", false, isPlaceholder: true);
                messages.Add(placeholderMessage);
                ScrollToBottom();

                // Start timeout tracking and timing
                pendingResponseMessage = placeholderMessage;
                pendingResponseStartTime = (float)Raylib.GetTime();
                lastRequestSentTime = DateTime.UtcNow;

                // Extract position context
                ChessPositionContext? context = contextExtractor?.Invoke();

                // Send automatic message with custom system prompt
                bool success = await backendApi.SendChatMessageAsync(gameId, message, context);

                if (!success)
                {
                    // Remove placeholder on failure
                    messages.Remove(placeholderMessage);
                    pendingResponseMessage = null;
                    isAutomaticMessagePending = false;
                    Log("❌ Failed to send automatic message - backend not connected", false, ConsoleColor.Red);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error sending automatic message: {ex.Message}", false, ConsoleColor.Red);
                isAutomaticMessagePending = false;
            }
        }

        private void OnReceiveChatResponse(object response)
        {
            try
            {
                // Clear timeout tracking and automatic message flag since we got a response
                pendingResponseMessage = null;
                isAutomaticMessagePending = false;

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
                string? audioBase64 = jsonElement.TryGetProperty("audio", out JsonElement audioProp) ? audioProp.GetString() : null;

                // Sanitize message for font compatibility (replace smart quotes/dashes with ASCII equivalents)
                message = SanitizeTextForFont(message);

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
                    VisibleCharCount = 0,
                    AudioBase64 = audioBase64
                };
                messages.Add(aiMessage);
                lastTypingUpdate = (float)Raylib.GetTime();

                // Play audio if available
                if (!string.IsNullOrEmpty(audioBase64) && audioService != null)
                {
                    _ = audioService.PlayBase64AudioAsync(audioBase64);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error processing chat response: {ex.Message}", false, ConsoleColor.Red);
                // Clear timeout tracking and automatic message flag
                pendingResponseMessage = null;
                isAutomaticMessagePending = false;
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

            // Clear timeout tracking and automatic message flag
            pendingResponseMessage = null;
            isAutomaticMessagePending = false;

            // Remove placeholder on error
            int placeholderIndex = messages.FindIndex(m => m.IsPlaceholder);
            if (placeholderIndex >= 0)
            {
                messages.RemoveAt(placeholderIndex);
            }

            AddMessage("System", $"Error: {error}", false);
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

        private void ScrollToBottom()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
            float scaledHeaderHeight = UIHelper.Scale(headerHeight);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            float messageAreaHeight = scaledSize.Y - scaledHeaderHeight - scaledInputHeight - scaledPadding * 2;
            float totalContentHeight = CalculateTotalContentHeight();

            // If content fits in view, no need to scroll
            if (totalContentHeight <= messageAreaHeight)
            {
                scrollOffset = 0f;
            }
            else
            {
                // Negative offset pushes content up, showing bottom
                scrollOffset = -(totalContentHeight - messageAreaHeight);
            }
        }

        private bool IsScrolledToBottom()
        {
            Vector2 scaledPos = GetPanelPosition();
            Vector2 scaledSize = GetPanelSize();
            float scaledHeaderHeight = UIHelper.Scale(headerHeight);
            float scaledInputHeight = UIHelper.Scale(inputHeight);
            float scaledPadding = UIHelper.Scale(messagePadding);

            float messageAreaHeight = scaledSize.Y - scaledHeaderHeight - scaledInputHeight - scaledPadding * 2;
            float totalContentHeight = CalculateTotalContentHeight();

            // If content fits in view, we're always at bottom
            if (totalContentHeight <= messageAreaHeight)
            {
                return true;
            }

            // Calculate what the bottom scroll position should be
            float bottomScrollOffset = -(totalContentHeight - messageAreaHeight);

            // Consider "at bottom" if within 10 pixels of bottom position
            return scrollOffset <= bottomScrollOffset + 10f;
        }

        private string SanitizeTextForFont(string text)
        {
            // Replace Unicode punctuation with ASCII equivalents for SDF font compatibility
            return text
                .Replace('\u2019', '\'')  // Smart single quote → apostrophe (39)
                .Replace('\u2018', '\'')  // Smart opening single quote → apostrophe (39)
                .Replace('\u201C', '"')   // Smart opening double quote → quote (34)
                .Replace('\u201D', '"')   // Smart closing double quote → quote (34)
                .Replace('\u2013', '-')   // En dash → hyphen (45)
                .Replace('\u2014', '-')   // Em dash → hyphen (45)
                .Replace("\u2026", "...") // Ellipsis → three dots
                .Replace('\u2022', '*');  // Bullet → asterisk (42)
        }
    }
}