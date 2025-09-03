# SignalBeam Platform Gateway Service
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Create non-root user
RUN groupadd -r signalbeam && useradd -r -g signalbeam signalbeam

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files
COPY ["SignalBeam.Platform.Gateway/SignalBeam.Platform.Gateway.csproj", "SignalBeam.Platform.Gateway/"]
COPY ["SignalBeam.Platform.ServiceDefaults/SignalBeam.Platform.ServiceDefaults.csproj", "SignalBeam.Platform.ServiceDefaults/"]

# Restore packages
RUN dotnet restore "SignalBeam.Platform.Gateway/SignalBeam.Platform.Gateway.csproj"

# Copy source code
COPY . .

# Build application
WORKDIR "/src/SignalBeam.Platform.Gateway"
RUN dotnet build "SignalBeam.Platform.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish application
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SignalBeam.Platform.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app

# Copy published app
COPY --from=publish /app/publish .

# Set ownership and permissions
RUN chown -R signalbeam:signalbeam /app
USER signalbeam

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SignalBeam.Platform.Gateway.dll"]