FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["WatchStats.Cli/WatchStats.Cli.csproj", "WatchStats.Cli/"]
COPY ["WatchStats.Core/WatchStats.Core.csproj", "WatchStats.Core/"]
RUN dotnet restore "WatchStats.Cli/WatchStats.Cli.csproj"
COPY . .
WORKDIR "/src/WatchStats.Cli"
RUN dotnet build "./WatchStats.Cli.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WatchStats.Cli.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WatchStats.Cli.dll"]
CMD ["./"]