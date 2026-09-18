# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is the management root (dockerfile:
# CustomerService/Dockerfile), so the csproj path is the service subdirectory
# and the sibling Common project referenced by CustomerService.csproj is
# inside the context for restore.
COPY ["CustomerService/CustomerService.csproj", "CustomerService/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "CustomerService/CustomerService.csproj"

COPY . .
RUN dotnet build "CustomerService/CustomerService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CustomerService/CustomerService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# aspnet:8.0 (Debian) ships no curl — required for the HEALTHCHECK below.
# Runs BEFORE the publish COPY so code-only rebuilds reuse this cached layer
# instead of re-downloading from Debian mirrors on every iteration.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# SQLite DB lands at /app/data/customers.db (see Program.cs config-honoring
# change + compose ConnectionStrings__DefaultConnection + ./CustomerService/data mount)
RUN mkdir -p /app/data

COPY --from=publish /app/publish .

# Kestrel binds 80 in-container (docs/GATEWAY.md rewrite: customerservice:80);
# compose's ASPNETCORE_URLS repeats this — the ENV makes the image self-sufficient.

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:${PORT:-80}/health || exit 1

EXPOSE 80

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=${ASPNETCORE_URLS:-http://+:${PORT:-80}} dotnet CustomerService.dll"]
