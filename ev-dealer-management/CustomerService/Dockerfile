# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# compose build context is ./CustomerService, so the csproj is at context root
COPY ["CustomerService.csproj", "./"]
RUN dotnet restore "CustomerService.csproj"

COPY . .
RUN dotnet build "CustomerService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CustomerService.csproj" -c Release -o /app/publish /p:UseAppHost=false

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
ENV ASPNETCORE_URLS=http://+:80

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1

EXPOSE 80

ENTRYPOINT ["dotnet", "CustomerService.dll"]
