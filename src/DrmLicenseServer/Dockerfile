FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/DrmLicenseServer/DrmLicenseServer.csproj", "DrmLicenseServer/"]
COPY ["src/Shared/Nexa.Shared.csproj", "Shared/"]
RUN dotnet restore "DrmLicenseServer/DrmLicenseServer.csproj"
COPY src/ .
WORKDIR "/src/DrmLicenseServer"
RUN dotnet build "DrmLicenseServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DrmLicenseServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DrmLicenseServer.dll"]
