FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY JobHunterAI.Web/*.csproj JobHunterAI.Web/
RUN dotnet restore JobHunterAI.Web/JobHunterAI.Web.csproj
COPY . .
RUN dotnet publish JobHunterAI.Web/JobHunterAI.Web.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobHunterAI.Web.dll"]
