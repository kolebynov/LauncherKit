FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY global.json ./
COPY kit/Directory.Build.props kit/Directory.Packages.props kit/Rules.ruleset kit/
COPY kit/src/LauncherKit.FeedEditor/ kit/src/LauncherKit.FeedEditor/
RUN dotnet publish kit/src/LauncherKit.FeedEditor/LauncherKit.FeedEditor.csproj \
    --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "LauncherKit.FeedEditor.dll"]
