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

ENV ASPNETCORE_URLS=http://+:80

# SQLite Migrate() at Program.cs:91 writes Data Source=/app/data/users.db. Compose
# masks a missing dir via the bind mount; without it (docker run, or a host that
# does not mount /app/data) the file cannot be created and startup throws before
# /health can answer. Every sibling service already has this line.
RUN mkdir -p /app/data

# /health is the MapHealthChecks liveness probe (Program.cs:118) the gateway's
# aggregate /health polls (APIGatewayService/Program.cs:286-312 probes /health on
# every upstream); userservice was the only service of seven without a probe.
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "UserService.dll"]
