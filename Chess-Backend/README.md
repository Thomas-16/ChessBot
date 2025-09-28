# Chess Backend API

A real-time chess chat API built with ASP.NET Core 8.0, SignalR, PostgreSQL, OpenAI, and ElevenLabs.

## Features

- 🎯 Real-time chat with AI chess opponent via SignalR
- 🗣️ Text-to-speech for AI responses using ElevenLabs
- 🎤 Speech-to-text for voice input using OpenAI Whisper
- 🗄️ PostgreSQL database for storing games and chat messages
- 🚀 Ready for Render deployment

## Quick Start

### Prerequisites

- .NET 8.0 SDK
- PostgreSQL database (local or hosted)
- OpenAI API key
- ElevenLabs API key

### Setup

1. Clone the repository
2. Navigate to the Chess-Backend directory
3. Copy `.env.example` to `.env` and fill in your API keys:

```bash
cp .env.example .env
```

4. Update your environment variables:

```env
OPENAI_API_KEY=your_openai_api_key_here
ELEVENLABS_API_KEY=your_elevenlabs_api_key_here
DATABASE_URL=postgres://username:password@localhost:5432/chessdb
```

5. Install dependencies:

```bash
dotnet restore
```

6. Run database migrations:

```bash
dotnet ef database update
```

7. Start the application:

```bash
dotnet run
```

The API will be available at `http://localhost:5000` with SignalR hub at `/chesshub`.

## API Endpoints

### SignalR Hub Methods (`/chesshub`)

- `StartNewGame()` - Creates a new game session and returns game ID
- `SendChatMessage(gameId, message)` - Send text message to AI
- `SendVoiceMessage(gameId, base64Audio)` - Send voice message to AI
- `SaveGameResult(gameId, pgn, result)` - Save game result to database
- `GetChatHistory(gameId)` - Retrieve chat history for a game

### HTTP Endpoints

- `GET /` - API status check
- `GET /health` - Health check endpoint

## Deployment on Render

1. Connect your GitHub repository to Render
2. Set the following environment variables in Render:
   - `OPENAI_API_KEY`
   - `ELEVENLABS_API_KEY`
   - `DATABASE_URL` (automatically provided by Render PostgreSQL)

3. Render will automatically:
   - Install dependencies with `dotnet restore`
   - Build the application with `dotnet build`
   - Run the application

## Project Structure

```
Chess-Backend/
├── Data/
│   └── ChessDbContext.cs          # Entity Framework database context
├── Hubs/
│   └── ChessHub.cs                # SignalR hub for real-time communication
├── Models/
│   ├── Game.cs                    # Game entity model
│   └── ChatMessage.cs             # Chat message entity model
├── Services/
│   ├── AIService.cs               # OpenAI integration service
│   └── VoiceService.cs            # Text-to-speech and speech-to-text service
├── Migrations/                    # Entity Framework migrations
├── Program.cs                     # Application configuration and startup
└── Chess-Backend.csproj           # Project file with dependencies
```

## Dependencies

- **Microsoft.AspNetCore.SignalR** - Real-time web functionality
- **Npgsql.EntityFrameworkCore.PostgreSQL** - PostgreSQL database provider
- **Microsoft.EntityFrameworkCore** - Object-relational mapping
- **OpenAI** - Official OpenAI .NET SDK
- **Microsoft.Extensions.Http** - HTTP client factory

## Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `OPENAI_API_KEY` | OpenAI API key for chat responses | Yes |
| `ELEVENLABS_API_KEY` | ElevenLabs API key for text-to-speech | Yes |
| `DATABASE_URL` | PostgreSQL connection string | Yes |
| `PORT` | Port number (auto-set by Render) | No |

## Database Schema

### Games Table
- `Id` (string, PK) - Unique game identifier
- `PGN` (string) - Portable Game Notation of the chess game
- `Result` (string) - Game result (e.g., "1-0", "0-1", "1/2-1/2")
- `StartTime` (DateTime) - When the game started
- `EndTime` (DateTime?) - When the game ended (nullable)

### ChatMessages Table
- `Id` (int, PK) - Auto-incrementing message ID
- `GameId` (string, FK) - References Games.Id
- `Speaker` (string) - "User" or "AI"
- `Message` (string) - Chat message content
- `Timestamp` (DateTime) - When the message was sent

## License

This project is part of the ChessBot application.