using Chess_Backend.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

namespace Chess_Backend.Services;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ChessDbContext _context;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(ChessDbContext context, ILogger<DatabaseHealthCheck> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to connect to the database with a timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await _context.Database.CanConnectAsync(timeoutCts.Token);

            // Don't log successful health checks to reduce noise
            return HealthCheckResult.Healthy("Database connection successful");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Database health check timed out");
            return HealthCheckResult.Degraded("Database connection timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Degraded($"Database connection failed: {ex.Message}");
        }
    }
}