using Chess_Backend.Data;
using Chess_Backend.Hubs;
using Chess_Backend.Services;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure port for Render hosting
string port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    // Increase max message size to 10MB for voice messages (WAV audio can be large)
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
});

// Configure CORS for all origins (adjust for production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure PostgreSQL database
string connectionString = GetConnectionString(builder.Configuration);
builder.Services.AddDbContext<ChessDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register services
builder.Services.AddSingleton<OpenAIService>();
builder.Services.AddScoped<GroqService>();
builder.Services.AddHttpClient<GroqService>();
builder.Services.AddScoped<VoiceService>();
builder.Services.AddHttpClient<VoiceService>();

// Add health checks
builder.Services.AddScoped<DatabaseHealthCheck>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded)
    .AddCheck("api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), new[] { "api" });

// Logging - configuration comes from appsettings.json

WebApplication app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.UseCors();

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("api")
});

// Map SignalR hub
app.MapHub<ChessHub>("/chesshub");

app.MapGet("/", () => "Chess Backend API is running!");

// Ensure database is created (only if database is available)
try
{
    using (IServiceScope scope = app.Services.CreateScope())
    {
        ChessDbContext context = scope.ServiceProvider.GetRequiredService<ChessDbContext>();
        context.Database.EnsureCreated();
    }
}
catch (Exception ex)
{
    ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Could not connect to database. Continuing without database initialization...");
}

app.Run();

// Helper method to parse DATABASE_URL for Render
static string GetConnectionString(IConfiguration configuration)
{
    string? databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

    if (!string.IsNullOrEmpty(databaseUrl))
    {
        try
        {
            // Parse Render DATABASE_URL format: postgres://user:password@host:port/database
            Uri uri = new Uri(databaseUrl);
            string[] userInfo = uri.UserInfo.Split(':');
            int port = uri.Port != -1 ? uri.Port : 5432; // Default PostgreSQL port if not specified

            string connectionString = $"Host={uri.Host};Port={port};Database={uri.LocalPath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=true;";

            Console.WriteLine($"Parsed connection string: Host={uri.Host};Port={port};Database={uri.LocalPath.TrimStart('/')};Username={userInfo[0]};Password=***;SSL Mode=Require;Trust Server Certificate=true;");
            return connectionString;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing DATABASE_URL: {ex.Message}");
            Console.WriteLine($"DATABASE_URL was: {databaseUrl}");
            throw;
        }
    }

    // Fall back to local connection string
    return configuration.GetConnectionString("DefaultConnection") ??
           "Host=localhost;Database=ChessDb;Username=postgres;Password=password;";
}