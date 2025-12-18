


# =========================
# Build stage
# =========================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy project files
COPY *.csproj ./

# Restore dependencies
RUN dotnet restore

# Copy all remaining files
COPY . ./

# Publish the project
RUN dotnet publish -c Release -o out

# =========================
# Runtime stage
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy published files from build stage
COPY --from=build /app/out ./

# Use environment variable PORT, fallback to 5000
ENV ASPNETCORE_URLS=http://+:$PORT
EXPOSE $PORT

# Start the application
ENTRYPOINT ["dotnet", "WhatsAppFlowApi.dll"]
