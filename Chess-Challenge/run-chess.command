#!/bin/bash
# Get the directory where this script is located
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Change to the Chess-Challenge directory
cd "$DIR"

# Run the game
dotnet run --project Chess-Challenge.csproj
