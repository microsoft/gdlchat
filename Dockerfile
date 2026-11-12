FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY gdlchat.csproj ./
RUN dotnet restore gdlchat.csproj

COPY . ./
RUN dotnet publish gdlchat.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false
RUN test -s /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish ./
USER $APP_UID

ENTRYPOINT ["dotnet", "gdlchat.dll"]