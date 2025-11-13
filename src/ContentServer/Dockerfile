FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["src/ContentServer/ContentServer.csproj", "ContentServer/"]
COPY ["src/Shared/Nexa.Shared.csproj", "Shared/"]

RUN dotnet restore "ContentServer/ContentServer.csproj"

COPY ["src/ContentServer/", "ContentServer/"]
COPY ["src/Shared/", "Shared/"]

WORKDIR "/src/ContentServer"

RUN dotnet build "ContentServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ContentServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

RUN apt-get update && \
    apt-get install -y curl && \
    rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

HEALTHCHECK --interval=10s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ContentServer.dll"]