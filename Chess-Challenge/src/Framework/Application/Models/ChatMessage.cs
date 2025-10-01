using System;

namespace ChessChallenge.Application.Models
{
    public class ChatMessage
    {
        public string Sender { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsFromUser { get; set; }
        public bool IsPlaceholder { get; set; }

        // For typing effect
        public int VisibleCharCount { get; set; }
        public bool IsTyping { get; set; }
        public string? AudioBase64 { get; set; }

        public ChatMessage(string sender, string message, bool isFromUser, bool isPlaceholder = false)
        {
            Sender = sender;
            Message = message;
            Timestamp = DateTime.Now;
            IsFromUser = isFromUser;
            IsPlaceholder = isPlaceholder;
            VisibleCharCount = isFromUser ? message.Length : 0; // User messages show immediately
            IsTyping = false;
        }

        public string GetVisibleMessage()
        {
            if (IsFromUser || IsPlaceholder || !IsTyping)
            {
                return Message;
            }

            return Message.Substring(0, Math.Min(VisibleCharCount, Message.Length));
        }
    }
}