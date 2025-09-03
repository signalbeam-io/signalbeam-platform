#!/bin/bash
# Update Go dependencies for SignalBeam Edge Collector

set -e

echo "🔄 Updating Go dependencies for SignalBeam Edge Collector..."

cd src/edge-agents/signalbeam-collector

echo "📦 Running go mod tidy..."
go mod tidy

echo "🔍 Verifying dependencies..."
go mod verify

echo "💾 Downloading dependencies..."
go mod download

echo "✅ Go dependencies updated successfully!"
echo ""
echo "Files updated:"
echo "  - go.mod"
echo "  - go.sum"
echo ""
echo "💡 Tip: Commit these changes to ensure CI/CD pipeline works correctly."