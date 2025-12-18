

# Use official .NET 9 SDK image for building the app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Set working directory
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY *.sln .
COPY WhatsAppFlowApi/*.csproj ./WhatsAppFlowApi/
RUN dotnet restore

# Copy everything else and build
COPY WhatsAppFlowApi/. ./WhatsAppFlowApi/
WORKDIR /src/WhatsAppFlowApi
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Use official .NET runtime image for running the app
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Expose default port
EXPOSE 80
EXPOSE 443

# Entry point
ENTRYPOINT ["dotnet", "WhatsAppFlowApi.dll"]
