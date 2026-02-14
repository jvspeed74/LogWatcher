FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LogWatcher.Seeder/LogWatcher.Seeder.csproj", "LogWatcher.Seeder/"]
RUN dotnet restore "LogWatcher.Seeder/LogWatcher.Seeder.csproj"
COPY . .
WORKDIR "/src/LogWatcher.Seeder"
RUN dotnet build "./LogWatcher.Seeder.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LogWatcher.Seeder.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LogWatcher.Seeder.dll"]
