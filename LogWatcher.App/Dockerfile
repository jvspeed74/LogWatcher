FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LogWatcher.App/LogWatcher.App.csproj", "LogWatcher.App/"]
COPY ["LogWatcher.Core/LogWatcher.Core.csproj", "LogWatcher.Core/"]
RUN dotnet restore "LogWatcher.App/LogWatcher.App.csproj"
COPY . .
WORKDIR "/src/LogWatcher.App"
RUN dotnet build "./LogWatcher.App.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LogWatcher.App.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LogWatcher.App.dll"]
CMD ["./"]