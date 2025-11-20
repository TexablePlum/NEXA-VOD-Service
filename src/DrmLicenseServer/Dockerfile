# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["src/DrmLicenseServer/DrmLicenseServer.csproj", "DrmLicenseServer/"]
COPY ["src/Shared/Nexa.Shared.csproj", "Shared/"]
RUN dotnet restore "DrmLicenseServer/DrmLicenseServer.csproj"

# Copy source code and build
COPY src/DrmLicenseServer/ DrmLicenseServer/
COPY src/Shared/ Shared/
WORKDIR "/src/DrmLicenseServer"
RUN dotnet build "DrmLicenseServer.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DrmLicenseServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy published app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "DrmLicenseServer.dll"]
