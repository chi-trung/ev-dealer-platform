# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is the management root (dockerfile:
# VehicleService/Dockerfile): the csproj path is the service subdirectory and
# the sibling Common project is inside the context for restore.
COPY ["VehicleService/VehicleService.csproj", "VehicleService/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "VehicleService/VehicleService.csproj"

# Copy the rest of the application code
COPY . .

# Build the application
RUN dotnet build "VehicleService/VehicleService.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "VehicleService/VehicleService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official .NET 8 runtime image to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create data directory for SQLite database
RUN mkdir -p /app/data

# aspnet:8.0 (Debian) ships no curl — without this the HEALTHCHECK below
# always fails and the container reads "unhealthy" forever. Runs BEFORE the
# publish COPY so code-only rebuilds reuse this cached layer instead of
# re-downloading from Debian mirrors on every iteration.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Add health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Expose port 8080
EXPOSE 8080

# Set the entry point
ENTRYPOINT ["dotnet", "VehicleService.dll"]
