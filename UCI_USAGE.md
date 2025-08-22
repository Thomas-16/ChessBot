# UCI Engine Usage Guide

## Building and Running

### GUI Mode (Development)
```bash
cd Chess-Challenge
dotnet run
```

### UCI Engine Mode
```bash
cd Chess-Challenge
dotnet run -- --uci
```
or
```bash
cd Chess-Challenge
dotnet run -- uci
```

### Publishing Single Executable
```bash
cd Chess-Challenge
dotnet publish -c Release -r win-x64 --self-contained false
dotnet publish -c Release -r linux-x64 --self-contained false
dotnet publish -c Release -r osx-x64 --self-contained false
```

## UCI Engine Usage

### Command Line Testing
You can test the UCI engine manually:
```bash
dotnet run -- --uci
```

Then type UCI commands:
```
uci
isready
position startpos moves e2e4 e7e5
go movetime 2000
quit
```

### Integration with Chess GUIs

#### Arena Chess GUI
1. Build the project in Release mode
2. In Arena: Engines → Install New Engine
3. Browse to the executable (Chess-Challenge.exe)
4. Command line parameters: `--uci`

#### Other UCI-compatible GUIs
- ChessBase
- Fritz
- Stockfish GUI
- PyChess
- Lucas Chess

## Implementation Details

### Mode Detection
- **GUI Mode**: Default when no arguments provided
- **UCI Mode**: Triggered by `--uci` or `uci` command line argument

### Time Management
- Uses 1/30th of remaining time (max 5 seconds, min 100ms)
- Respects UCI `movetime` command for fixed time per move
- Handles `wtime`/`btime` for time control games

### Features Supported
- Full UCI protocol implementation
- Position setup via FEN or moves
- Time controls (wtime, btime, movetime)
- Search interruption with `stop` command
- Opening book integration
- Transposition tables
- Advanced evaluation

### Development Workflow
1. Develop and test in GUI mode: `dotnet run`
2. Test UCI mode: `dotnet run -- --uci`
3. All bot logic remains in `src/My Bot/` folder
4. Changes to MainBot.cs automatically work in both modes