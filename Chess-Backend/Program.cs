using Chess_Backend.Data;
using Chess_Backend.Hubs;
using Chess_Backend.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure port for Render hosting
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddSignalR();

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
var connectionString = GetConnectionString(builder.Configuration);
builder.Services.AddDbContext<ChessDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register services
builder.Services.AddScoped<AIService>();
builder.Services.AddScoped<VoiceService>();
builder.Services.AddHttpClient<VoiceService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ChessDbContext>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.UseCors();

// Health check endpoint for Render
app.MapHealthChecks("/health");

// Map SignalR hub
app.MapHub<ChessHub>("/chesshub");

app.MapGet("/", () => "Chess Backend API is running!");

// Ensure database is created (only if database is available)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ChessDbContext>();
        context.Database.EnsureCreated();
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Could not connect to database. Continuing without database initialization...");
}

app.Run();

// Helper method to parse DATABASE_URL for Render
static string GetConnectionString(IConfiguration configuration)
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

    if (!string.IsNullOrEmpty(databaseUrl))
    {
        // Parse Render DATABASE_URL format: postgres://user:password@host:port/database
        var uri = new Uri(databaseUrl);
        var connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.LocalPath.TrimStart('/')};Username={uri.UserInfo.Split(':')[0]};Password={uri.UserInfo.Split(':')[1]};SSL Mode=Require;Trust Server Certificate=true;";
        return connectionString;
    }

    // Fall back to local connection string
    return configuration.GetConnectionString("DefaultConnection") ??
           "Host=localhost;Database=ChessDb;Username=postgres;Password=password;";
}