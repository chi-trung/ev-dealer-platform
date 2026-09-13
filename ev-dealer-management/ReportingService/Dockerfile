# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is ./ReportingService, so the csproj is at context root
COPY ["ReportingService.csproj", "./"]
RUN dotnet restore "ReportingService.csproj"

COPY . .
RUN dotnet build "ReportingService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ReportingService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# aspnet:8.0 (Debian) ships no curl — required for the HEALTHCHECK below
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# reporting_dev.db is relocated here via REPORTING_DB_PATH (see Program.cs
# change); compose mounts ./ReportingService/data at /app/data.
RUN mkdir -p /app/data

ENV ASPNETCORE_URLS=http://+:80

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1

EXPOSE 80

ENTRYPOINT ["dotnet", "ReportingService.dll"]
