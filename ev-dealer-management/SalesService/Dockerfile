# Use the official .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["SalesService.csproj", "./"]
RUN dotnet restore "SalesService.csproj"

# Copy the rest of the application code
COPY . .

# Build the application
RUN dotnet build "SalesService.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "SalesService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official .NET 8 runtime image to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create data directory for SQLite database
RUN mkdir -p /app/data

# aspnet:8.0 (Debian) ships no curl — without this the HEALTHCHECK below
# always fails and the container reads "unhealthy" forever
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Add health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:80/api/orders/health || exit 1

# Expose port 80
EXPOSE 80

# Set the entry point
ENTRYPOINT ["dotnet", "SalesService.dll"]

