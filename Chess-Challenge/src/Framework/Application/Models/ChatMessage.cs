using System;

namespace ChessChallenge.Application.Models
{
    public class ChatMessage
    {
        public string Sender { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsFromUser { get; set; }

        public ChatMessage(string sender, string message, bool isFromUser)
        {
            Sender = sender;
            Message = message;
            Timestamp = DateTime.Now;
            IsFromUser = isFromUser;
        }
    }
}