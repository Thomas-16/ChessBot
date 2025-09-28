using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Chess_Backend.Models;

public class ChatMessage
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string GameId { get; set; } = string.Empty;

    [Required]
    public string Speaker { get; set; } = string.Empty; // "User" or "AI"

    [Required]
    public string Message { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(GameId))]
    public virtual Game Game { get; set; } = null!;
}