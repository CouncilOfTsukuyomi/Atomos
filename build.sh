#!/bin/bash
# build.sh - Build script for PenumbraModForwarder

# Default parameters
CONFIGURATION="${1:-Release}"
RUNTIME="${2:-linux-x64}"
FRAMEWORK="net9.0"
OUTPUT_DIR="./publish/linux"

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== PenumbraModForwarder Build Script ===${NC}"
echo "Configuration: $CONFIGURATION"
echo "Runtime: $RUNTIME"
echo "Framework: $FRAMEWORK"
echo "Output Directory: $OUTPUT_DIR"
echo "Working from: $(dirname "$(realpath "$0")")"

# Check dotnet
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}dotnet CLI not found${NC}"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "Using .NET version: $DOTNET_VERSION"

# Create output directory
if [ ! -d "$OUTPUT_DIR" ]; then
    mkdir -p "$OUTPUT_DIR"
    echo "Created output directory"
fi

# Define projects (in the same directory as the script)
PROJECTS=(
    "Atomos.Watchdog"
    "Atomos.UI"
    "Atomos.ConsoleTooling"
    "Atomos.BackgroundWorker"
)

echo ""
echo "Verifying projects..."
for project in "${PROJECTS[@]}"; do
    if [ -d "$project" ]; then
        echo "Found: $project"
    else
        echo "Missing: $project"
    fi
done

echo ""
echo "Starting builds..."
SUCCESS_COUNT=0

for project in "${PROJECTS[@]}"; do
    echo ""
    echo "Publishing $project..."
    
    if dotnet publish "$project" -c "$CONFIGURATION" -p:PublishSingleFile=true --self-contained=true -p:DebugType=None -p:DebugSymbols=false -r "$RUNTIME" -o "$OUTPUT_DIR" -f "$FRAMEWORK"; then
        echo "SUCCESS: $project"
        ((SUCCESS_COUNT++))
    else
        echo "FAILED: $project"
    fi
done

echo ""
echo "=== Summary ==="
echo "Successful: $SUCCESS_COUNT of ${#PROJECTS[@]}"

if [ $SUCCESS_COUNT -eq ${#PROJECTS[@]} ]; then
    echo "All projects built successfully!"
    
    if [ -d "$OUTPUT_DIR" ]; then
        echo ""
        echo "Output files:"
        for file in "$OUTPUT_DIR"/*; do
            if [ -f "$file" ]; then
                SIZE_BYTES=$(stat -c%s "$file" 2>/dev/null || stat -f%z "$file" 2>/dev/null)
                SIZE_MB=$(echo "scale=2; $SIZE_BYTES / 1024 / 1024" | bc 2>/dev/null || echo "0")
                echo "$(basename "$file") - ${SIZE_MB} MB"
            fi
        done
    fi
    exit 0
else
    echo "Some builds failed!"
    exit 1
fi