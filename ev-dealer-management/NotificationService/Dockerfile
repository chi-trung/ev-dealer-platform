# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is ./NotificationService, so the csproj is at context root
COPY ["NotificationService.csproj", "./"]
RUN dotnet restore "NotificationService.csproj"

COPY . .
RUN dotnet build "NotificationService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NotificationService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# aspnet:8.0 (Debian) ships no curl — required for the HEALTHCHECK below.
# Runs BEFORE the publish COPY so code-only rebuilds reuse this cached layer
# instead of re-downloading from Debian mirrors on every iteration.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Serilog file sink writes Logs/notification-service-.log relative to /app;
# compose bind-mounts ./NotificationService/Logs over it (path is not
# env-overridable — bootstrap logger reads appsettings.json only).
RUN mkdir -p /app/Logs

COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:80

# /health is NotificationService's custom JSON liveness endpoint (Program.cs:70)
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1

EXPOSE 80

ENTRYPOINT ["dotnet", "NotificationService.dll"]
