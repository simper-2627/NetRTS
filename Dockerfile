FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/NetRts.Server/NetRts.Server.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /home/app && chown "$APP_UID:$APP_UID" /home/app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__sqlite="Data Source=/home/app/netrts.db"
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "NetRts.Server.dll"]
