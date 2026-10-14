FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["btclient/btclient.csproj", "btclient/"]
COPY ["boottorrent-lib/boottorrent-lib.csproj", "boottorrent-lib/"]
RUN dotnet restore "btclient/btclient.csproj"
COPY . .
RUN dotnet publish "btclient/btclient.csproj" -c Release -o /out /p:PublishAot=false /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN mkdir -p /app/logs /app/artifacts && chown -R $APP_UID:$APP_UID /app
USER $APP_UID
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /out .
ENTRYPOINT ["dotnet", "btclient.dll"]
