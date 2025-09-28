using Chess_Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Chess_Backend.Data;

public class ChessDbContext : DbContext
{
    public ChessDbContext(DbContextOptions<ChessDbContext> options) : base(options)
    {
    }

    public DbSet<Game> Games { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Game entity
        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.Result).HasMaxLength(20);
            entity.HasIndex(e => e.StartTime);
        });

        // Configure ChatMessage entity
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GameId).HasMaxLength(50);
            entity.Property(e => e.Speaker).HasMaxLength(10);
            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.Timestamp);

            // Configure relationship
            entity.HasOne(e => e.Game)
                  .WithMany(g => g.ChatMessages)
                  .HasForeignKey(e => e.GameId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}