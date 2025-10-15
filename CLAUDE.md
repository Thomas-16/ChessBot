# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a dual-project chess application consisting of:
- **Chess-Challenge**: Raylib-based chess GUI client (.NET 6.0) with AI chat features
- **Chess-Backend**: ASP.NET Core 8.0 backend hosted on Render with SignalR, PostgreSQL, OpenAI/Groq, and ElevenLabs TTS

## Build & Run Commands

### Frontend (Chess-Challenge)
```bash
# Build
cd Chess-Challenge
dotnet build

# Run (GUI mode - default)
dotnet run

# Run in UCI mode (for chess engines)
# Set UCI_MODE = true in Program.cs first
```

### Backend (Chess-Backend)
```bash
# Build
cd Chess-Backend
dotnet build

# Run locally
dotnet run

# Docker build (for Render deployment)
docker build -t chess-backend .
docker run -p 5000:5000 chess-backend
```

## Architecture

### Frontend Architecture (Chess-Challenge)

**Dual Mode Operation:**
- **GUI Mode** (default): Raylib-based visual chess game with real-time chat
- **UCI Mode**: Text-based Universal Chess Interface for engine integration

**Key Components:**
1. **Chess Engine** (`src/Main Bot/`)
   - `MainBot.cs`: Core chess AI with minimax + alpha-beta pruning
   - `Evaluation.cs`: Position evaluation (material, piece-square tables)
   - `TranspositionTable.cs`: Move caching for performance
   - `OpeningBook.cs`: Opening move database
   - `UCIEngine.cs`: UCI protocol implementation

2. **UI Layer** (`src/Framework/Application/UI/`)
   - `BoardUI.cs`: Chess board rendering with Raylib
   - `ChatUI.cs`: DM-style chat interface with typing effects
   - `MenuUI.cs`: Game controls and buttons
   - `UIHelper.cs`: Resolution-independent scaling utilities

3. **Services** (`src/Framework/Application/Services/`)
   - `SignalRService.cs`: Real-time WebSocket connection to backend
   - `BackendApiService.cs`: Wrapper for backend API calls
   - `GameStorageService.cs`: Game session and chat persistence
   - `AudioService.cs`: Microphone recording (NAudio) + TTS audio playback (plays MP3 from base64)
   - `GroqWhisperService.cs`: Speech-to-text via Groq Whisper API (~200-500ms)

4. **Core** (`src/Framework/Application/Core/`)
   - `ChallengeController.cs`: Main game controller, orchestrates UI/services
   - `Program.cs`: Entry point, DI setup, Raylib initialization

**Rendering:**
- Uses Raylib-cs for graphics (1920x1080 reference resolution)
- SDF (Signed Distance Field) fonts for crisp text at any scale
- All UI coordinates scaled via `UIHelper.Scale()` for resolution independence

### Backend Architecture (Chess-Backend)

**Tech Stack:**
- ASP.NET Core 8.0 with SignalR for real-time communication
- PostgreSQL with Entity Framework Core
- Groq API (llama-3.1-70b-versatile, ~300-600ms) with OpenAI fallback (gpt-4o-mini, ~2000ms)
- ElevenLabs for text-to-speech (~1800ms)

**Key Components:**
1. **SignalR Hub** (`Hubs/ChessHub.cs`)
   - `StartNewGame()`: Creates game session, returns gameId
   - `SendChatMessage(gameId, message)`:
     - Tries Groq first (fast), falls back to OpenAI
     - Generates TTS audio via ElevenLabs
     - Sends `ReceiveChatResponse` event with message + audio (base64)
   - `SaveGameResult(gameId, pgn, result)`: Persists completed games

2. **Services:**
   - `GroqService.cs`: Groq API integration (primary, fastest)
   - `OpenAIService.cs`: OpenAI integration (fallback)
   - `VoiceService.cs`: ElevenLabs TTS only (STT moved to frontend)
   - Both AI services use shared prompt from `Config/AIPrompts.cs`

3. **Database Models** (`Models/`)
   - `Game`: Chess game sessions (Id, PGN, Result, timestamps)
   - `ChatMessage`: Chat history (GameId, Speaker, Message, Timestamp)

**Connection Flow:**
1. Frontend connects to SignalR at startup
2. Game starts → backend creates session with gameId
3. User sends chat → Groq generates response (~300ms) + ElevenLabs TTS (~1800ms)
4. Backend sends text + audio back via SignalR event
5. Frontend displays typing effect + plays audio simultaneously
6. Game ends → frontend sends PGN to backend for storage

**Deployment (Render):**
- Uses Dockerfile for deployment (no native .NET support on Render)
- Environment variables: `DATABASE_URL`, `GROQ_API_KEY`, `OPENAI_API_KEY`, `ELEVENLABS_API_KEY`, `PORT`
- DATABASE_URL parsed from Render's PostgreSQL format: `postgres://user:pass@host:port/db`
- Health checks at `/health` and `/health/ready`

## Frontend-Backend Integration

**SignalR Events:**
- **Client → Server:**
  - `StartNewGame()` → returns gameId
  - `SendChatMessage(gameId, message)` → triggers AI response
  - `SaveGameResult(gameId, pgn, result)` → saves game

- **Server → Client:**
  - `ReceiveChatResponse` → { gameId, message, audio (base64), timestamp }
  - `ChatError` → error string
  - `GameSaved` → gameId confirmation

**Text Chat Flow:**
1. User types message → `ChatUI.SendMessage()`
2. Display "..." placeholder immediately
3. Send to backend via SignalR (~50ms network)
4. Backend processes:
   - Game validation: ~5ms
   - Groq API: ~300-600ms (or OpenAI fallback: ~2000ms)
   - Database save: ~85ms
   - ElevenLabs TTS: ~1800ms
5. Total backend time: ~2.2s (Groq) or ~4s (OpenAI)
6. Frontend receives response
7. Replace "..." with actual message
8. Start typing effect (30ms per character)
9. Play audio simultaneously (from base64 → temp file → Raylib music stream, no looping)

**Voice Chat Flow (Frontend STT):**
1. User clicks mic button → `AudioService.StartRecording()`
2. User speaks (NAudio records WAV at 16kHz mono)
3. User clicks mic again → `AudioService.StopRecording()` returns temp WAV path
4. Display "Transcribing..." indicator
5. Frontend calls Groq Whisper API directly (~200-500ms)
6. Display transcribed text as user message
7. Send transcribed text as normal chat message (same flow as text chat above)
8. Frontend receives AI response with TTS audio

**Configuration:**
- **Frontend** (`Chess-Challenge/appsettings.json`):
  - `Backend:ApiBaseUrl`: Backend URL for SignalR
  - `Groq:ApiKey`: Groq API key for Whisper STT (frontend-side transcription)
  - `Groq:WhisperModel`: "whisper-large-v3" (best quality, ~200-500ms)
- **Backend** (Environment variables on Render):
  - `GROQ_API_KEY`: For LLM chat responses
  - `OPENAI_API_KEY`: Fallback for chat
  - `ELEVENLABS_API_KEY`: TTS audio generation
  - `DATABASE_URL`: PostgreSQL connection

## Important Implementation Details

**Chat UI Typing Effect:**
- `ChatMessage.IsTyping` controls animation state
- `ChatMessage.VisibleCharCount` increments every 30ms
- `ChatMessage.GetVisibleMessage()` returns substring for rendering
- Audio plays in background via `AudioService.PlayBase64AudioAsync()`

**SignalR Handler Registration:**
- Handlers MUST be registered BEFORE `connection.StartAsync()`
- `ChatUI` registers handlers in constructor → stored in `SignalRService` fields
- When connection created, stored handlers are attached to connection
- This prevents timing issues where UI tries to register on null connection

**Audio Playback (No Looping):**
- Set `music.looping = false` BEFORE assigning to nullable field
- Cannot modify struct properties on `Music?` value type directly
- TTS audio auto-stops when playback completes

**Response Timeout:**
- 10-second timeout for chat responses
- Tracked via `pendingResponseMessage` and `pendingResponseStartTime`
- Timeout shows "Error responding right now" in chat

**Voice Input System:**
- Mic button disabled until Groq Whisper service is initialized
- `AudioService` records at 16kHz mono (optimized for Whisper)
- Saves recording to temp WAV file, deleted after transcription
- `GroqWhisperService` directly calls Groq API from frontend
- Transcribed text sent as regular chat message (unified flow)
- Max recording duration: 30 seconds (auto-stop)

**AI Prompt Consistency:**
- Both Groq and OpenAI use `AIPrompts.ChessOpponentSystemPrompt`
- Located in `Chess-Backend/Config/AIPrompts.cs`
- Change prompt there to affect both services

## Deployment Notes

**Backend (Render):**
1. Push changes to repo
2. Render auto-deploys from `Chess-Backend/Dockerfile`
3. Verify environment variables are set: GROQ_API_KEY, OPENAI_API_KEY, ELEVENLABS_API_KEY, DATABASE_URL, PORT
4. Check logs for performance timing (⏱️ emoji prefixed)

**Frontend:**
- Standalone .NET 6 executable
- Update `appsettings.json`:
  - `Backend:ApiBaseUrl`: Point to Render backend
  - `Groq:ApiKey`: Add Groq API key for voice input (get from https://console.groq.com)
- Run locally: `dotnet run` from Chess-Challenge directory
- Voice input requires Groq API key (free tier available)

## Performance Optimization

**Current Performance (Optimized):**
- **Voice transcription**: ~200-500ms (Groq Whisper API on frontend)
- **Chat LLM response**: ~300-600ms (Groq llama-3.1-70b)
- **TTS generation**: ~1800ms (ElevenLabs)
- **Total voice-to-response**: ~2.5-3s (fast!)

**Architecture Decisions:**
- **Frontend STT**: Voice transcription moved to frontend for 2-3s improvement
  - No backend upload/processing overhead
  - Groq Whisper API directly from client
  - Falls back to backend if Groq key not configured
- **Unified chat flow**: Voice transcribed text sent as regular message
  - Simplifies backend (no separate voice message handlers)
  - Same AI response flow for text and voice input

**Further Optimization Ideas:**
- Consider streaming: send text response immediately, stream audio separately
- Make TTS optional (toggle voice on/off)
- Cache frequent responses
