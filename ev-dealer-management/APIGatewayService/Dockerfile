# Use the official .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is the management root (dockerfile:
# APIGatewayService/Dockerfile). This service references no sibling project, so
# a service-local context would also have worked — the root context is used for
# parity with the other six and so the shared root .dockerignore applies.
COPY ["APIGatewayService/APIGatewayService.csproj", "APIGatewayService/"]
RUN dotnet restore "APIGatewayService/APIGatewayService.csproj"

COPY . .
RUN dotnet build "APIGatewayService/APIGatewayService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "APIGatewayService/APIGatewayService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# aspnet:8.0 (Debian) ships no curl — required for the HEALTHCHECK below.
# Runs BEFORE the publish COPY so code-only rebuilds reuse this cached layer
# instead of re-downloading from Debian mirrors on every iteration.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:80

# Liveness ONLY: the aggregate /health returns 503 until all six upstreams
# answer 2xx (Program.cs), so `curl -f` would mark a perfectly-routing gateway
# unhealthy. Any HTTP response, 200 or 503, proves it is alive.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -s -o /dev/null http://localhost:80/health || exit 1

EXPOSE 80

ENTRYPOINT ["dotnet", "APIGatewayService.dll"]
