# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["src/DRMServer/DRMServer.csproj", "DRMServer/"]
COPY ["src/Shared/Nexa.Shared.csproj", "Shared/"]
RUN dotnet restore "DRMServer/DRMServer.csproj"

# Copy source code and build
COPY src/DRMServer/ DRMServer/
COPY src/Shared/ Shared/
WORKDIR "/src/DRMServer"
RUN dotnet build "DRMServer.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DRMServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy published app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "DRMServer.dll"]
