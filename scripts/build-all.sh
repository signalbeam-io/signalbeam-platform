#!/bin/bash
# Build all SignalBeam Platform components locally

set -e

echo "🚀 Building SignalBeam Platform Components..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if Docker is running
if ! docker info >/dev/null 2>&1; then
    print_error "Docker is not running. Please start Docker and try again."
    exit 1
fi

# Build .NET Backend Services
print_status "Building .NET Backend Services..."
if dotnet build src/backend/SignalBeam.Platform.sln --configuration Release; then
    print_success "Backend services built successfully"
else
    print_error "Failed to build backend services"
    exit 1
fi

# Test Backend Services
print_status "Running .NET tests..."
if dotnet test src/backend/SignalBeam.Platform.sln --no-build --configuration Release --verbosity minimal; then
    print_success "All backend tests passed"
else
    print_warning "Some backend tests failed (continuing...)"
fi

# Build Edge Collector
print_status "Building Edge Collector (Go)..."
cd src/edge-agents/signalbeam-collector

if go mod tidy && go test ./... && go build -o signalbeam-collector ./cmd; then
    print_success "Edge collector built successfully"
    cd ../../..
else
    print_error "Failed to build edge collector"
    cd ../../..
    exit 1
fi

# Build Frontend
print_status "Building Frontend (React)..."
cd src/frontend/signalbeam-ui

if npm ci && npm run type-check && npm run lint && npm test && npm run build; then
    print_success "Frontend built successfully"
    cd ../../..
else
    print_error "Failed to build frontend"
    cd ../../..
    exit 1
fi

# Build Docker Images (Development)
print_status "Building Docker images for development..."
if docker-compose -f docker-compose.yml -f docker-compose.dev.yml build; then
    print_success "All Docker images built successfully"
else
    print_error "Failed to build Docker images"
    exit 1
fi

print_success "🎉 All SignalBeam Platform components built successfully!"

echo ""
echo "📋 Next steps:"
echo "   • Start infrastructure: docker-compose up -d"
echo "   • Start services: docker-compose -f docker-compose.yml -f docker-compose.dev.yml up -d"
echo "   • View logs: docker-compose logs -f"
echo "   • Access UI: http://localhost:3001"
echo "   • Access API Gateway: http://localhost:8080"