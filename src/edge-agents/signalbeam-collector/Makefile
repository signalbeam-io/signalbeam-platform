# SignalBeam Edge Collector Makefile

.PHONY: build build-all test clean deps fmt lint

# Default target
all: build

# Build for current platform
build:
	go build -o bin/signalbeam-collector ./cmd

# Build for all platforms
build-all: clean
	mkdir -p dist
	
	# Linux AMD64
	GOOS=linux GOARCH=amd64 go build -o dist/signalbeam-collector-linux-amd64 ./cmd
	
	# Linux ARM64 (Raspberry Pi 4/5)
	GOOS=linux GOARCH=arm64 go build -o dist/signalbeam-collector-linux-arm64 ./cmd
	
	# Linux ARM32 (Raspberry Pi 2/3)  
	GOOS=linux GOARCH=arm GOARM=7 go build -o dist/signalbeam-collector-linux-arm ./cmd
	
	# Windows AMD64
	GOOS=windows GOARCH=amd64 go build -o dist/signalbeam-collector-windows-amd64.exe ./cmd
	
	# macOS AMD64
	GOOS=darwin GOARCH=amd64 go build -o dist/signalbeam-collector-darwin-amd64 ./cmd
	
	# macOS ARM64
	GOOS=darwin GOARCH=arm64 go build -o dist/signalbeam-collector-darwin-arm64 ./cmd

# Run tests
test:
	go test -v ./...

# Run tests with coverage
test-coverage:
	go test -v -coverprofile=coverage.out ./...
	go tool cover -html=coverage.out -o coverage.html

# Install dependencies
deps:
	go mod download
	go mod tidy

# Format code
fmt:
	go fmt ./...

# Lint code (requires golangci-lint)
lint:
	golangci-lint run

# Clean build artifacts
clean:
	rm -rf bin/
	rm -rf dist/
	rm -f coverage.out coverage.html

# Run the collector locally
run:
	go run ./cmd -config config.yaml

# Run with debug logging
run-debug:
	go run ./cmd -config config.yaml

# Install golangci-lint
install-lint:
	curl -sSfL https://raw.githubusercontent.com/golangci/golangci-lint/master/install.sh | sh -s -- -b $(shell go env GOPATH)/bin

# Package releases
package: build-all
	cd dist && \
	tar -czf signalbeam-collector-linux-amd64.tar.gz signalbeam-collector-linux-amd64 && \
	tar -czf signalbeam-collector-linux-arm64.tar.gz signalbeam-collector-linux-arm64 && \
	tar -czf signalbeam-collector-linux-arm.tar.gz signalbeam-collector-linux-arm && \
	tar -czf signalbeam-collector-darwin-amd64.tar.gz signalbeam-collector-darwin-amd64 && \
	tar -czf signalbeam-collector-darwin-arm64.tar.gz signalbeam-collector-darwin-arm64 && \
	zip signalbeam-collector-windows-amd64.zip signalbeam-collector-windows-amd64.exe

# Development server with auto-reload (requires air)
dev:
	air

# Install air for development
install-air:
	go install github.com/cosmtrek/air@latest