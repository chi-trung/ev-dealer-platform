FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is the management root (dockerfile:
# UserService/Dockerfile): the csproj path is the service subdirectory and
# the sibling Common project is inside the context for restore. Copy both
# csproj first so restore is its own cached layer.
COPY ["UserService/UserService.csproj", "UserService/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "UserService/UserService.csproj"

# copy everything else and publish
COPY . .
RUN dotnet publish "UserService/UserService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# aspnet:8.0 (Debian) ships no curl — required for the HEALTHCHECK below.
# Runs BEFORE the publish COPY so code-only rebuilds reuse this cached layer
# instead of re-downloading from Debian mirrors on every iteration
# (same layer the other six services already have).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# SQLite db.Database.Migrate() writes Data Source=/app/data/users.db. Compose
# masks a missing dir via the bind mount; without it (docker run, or a host that
# does not mount /app/data) the file cannot be created and startup throws before
# /health can answer. Every sibling service already has this line.
# Named by symbol, not line number: a line cite here drifted (see issue #131).
RUN mkdir -p /app/data

# /health is the app.MapHealthChecks("/health") liveness probe the gateway's
# aggregate /health polls — the HcHealthCheckWriter.Services table in
# APIGatewayService/Program.cs (~:289, a static array literal) lists every
# upstream it pings. userservice was the only service of seven without a
# probe. Line numbers drift; grep the symbol instead.
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:${PORT:-80}/health || exit 1

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=${ASPNETCORE_URLS:-http://+:${PORT:-80}} dotnet UserService.dll"]
