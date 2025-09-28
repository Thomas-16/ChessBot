using System.ComponentModel.DataAnnotations;

namespace Chess_Backend.Models;

public class Game
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string PGN { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime? EndTime { get; set; }

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
}