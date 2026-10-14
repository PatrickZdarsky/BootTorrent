FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN mkdir -p /app/logs /app/tmp/artifacts /app/tmp/uploads && chown -R $APP_UID:$APP_UID /app
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["btserver/btserver.csproj", "btserver/"]
COPY ["boottorrent-lib/boottorrent-lib.csproj", "boottorrent-lib/"]
RUN dotnet restore "btserver/btserver.csproj"
COPY . .
WORKDIR "/src/btserver"
RUN dotnet build "btserver.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "btserver.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "btserver.dll"]
