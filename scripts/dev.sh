#!/bin/bash

# SignalBeam Platform Development Script
set -e

echo "🚀 Starting SignalBeam Platform Development Environment"

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
if ! docker info > /dev/null 2>&1; then
    print_error "Docker is not running. Please start Docker and try again."
    exit 1
fi

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK is not installed. Please install .NET 9 and try again."
    exit 1
fi

# Check if Node.js is installed
if ! command -v node &> /dev/null; then
    print_error "Node.js is not installed. Please install Node.js and try again."
    exit 1
fi

print_status "Starting infrastructure services with Docker Compose..."
docker-compose up -d

print_status "Waiting for services to be healthy..."
sleep 10

# Function to wait for service
wait_for_service() {
    local service_name=$1
    local port=$2
    local max_attempts=30
    local attempt=1

    print_status "Waiting for $service_name on port $port..."
    
    while [ $attempt -le $max_attempts ]; do
        if nc -z localhost $port 2>/dev/null; then
            print_success "$service_name is ready!"
            return 0
        fi
        sleep 2
        attempt=$((attempt + 1))
    done
    
    print_error "$service_name failed to start on port $port"
    return 1
}

# Wait for key services
wait_for_service "PostgreSQL" 5432
wait_for_service "Redis" 6379
wait_for_service "ClickHouse" 8123
wait_for_service "RabbitMQ" 5672

print_status "Building .NET Aspire solution..."
cd src/backend
dotnet restore
dotnet build

print_success "Infrastructure is ready! 🎉"

echo ""
echo "📊 Access URLs:"
echo "  • Aspire Dashboard: https://localhost:15888"
echo "  • Grafana: http://localhost:3000 (admin/admin)"
echo "  • Prometheus: http://localhost:9090"
echo "  • Jaeger: http://localhost:16686"
echo "  • RabbitMQ Management: http://localhost:15672 (signalbeam/signalbeam_password)"
echo "  • ClickHouse: http://localhost:8123"
echo ""

print_status "Starting .NET Aspire AppHost..."
dotnet run --project SignalBeam.Platform.AppHost

echo ""
print_success "SignalBeam Platform is running! 🌟"