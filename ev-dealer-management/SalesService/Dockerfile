# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is the management root (dockerfile:
# SalesService/Dockerfile): the csproj path is the service subdirectory and
# the sibling Common project is inside the context for restore.
COPY ["SalesService/SalesService.csproj", "SalesService/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "SalesService/SalesService.csproj"

# Copy the rest of the application code
COPY . .

# Build the application
RUN dotnet build "SalesService/SalesService.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "SalesService/SalesService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official .NET 8 runtime image to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create data directory for SQLite database
RUN mkdir -p /app/data

# aspnet:8.0 (Debian) ships no curl — without this the HEALTHCHECK below
# always fails and the container reads "unhealthy" forever. Runs BEFORE the
# publish COPY so code-only rebuilds reuse this cached layer instead of
# re-downloading from Debian mirrors on every iteration.
# Issue #49: libfontconfig1 + fonts-dejavu are necessary but NOT the whole
# Linux story — libSkiaSharp.so/libHarfBuzzSharp.so themselves come from the
# SkiaSharp.NativeAssets.Linux.NoDependencies + HarfBuzzSharp.NativeAssets.Linux
# PackageReferences in SalesService.csproj (QuestPDF's net8 target ships only
# Win32/macOS natives; CI proved a fonts-only image still throws). This layer
# supplies what the native libs link at load (fontconfig) and the glyph coverage
# for the template's Vietnamese diacritics (DejaVu = full Latin Extended set).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libfontconfig1 fonts-dejavu \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Health check. SalesService actually maps BOTH /health (the
# app.MapHealthChecks("/health") call, the one the gateway's aggregate
# probe hits) and /api/orders/health (OrdersController's
# [Route("api/[controller]")] + [HttpGet("health")], an anonymous 200) —
# either answers 200 to curl -f. /health is the canonical one the gateway
# polls and the other six use. Named by symbol, not line number: a line
# cite here drifted three times already (see issue #131).
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:${PORT:-80}/health || exit 1

# Expose port 80
EXPOSE 80

# Set the entry point
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=${ASPNETCORE_URLS:-http://+:${PORT:-80}} dotnet SalesService.dll"]

