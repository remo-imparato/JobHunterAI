FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY JobHunterAI.Domain/*.csproj JobHunterAI.Domain/
COPY JobHunterAI.Application/*.csproj JobHunterAI.Application/
COPY JobHunterAI.Infrastructure/*.csproj JobHunterAI.Infrastructure/
COPY JobHunterAI.AI/*.csproj JobHunterAI.AI/
COPY JobHunterAI.Automation/*.csproj JobHunterAI.Automation/
COPY JobHunterAI.Api/*.csproj JobHunterAI.Api/
RUN dotnet restore JobHunterAI.Api/JobHunterAI.Api.csproj
COPY . .
RUN dotnet publish JobHunterAI.Api/JobHunterAI.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/logs && chown "$APP_UID" /app/logs
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobHunterAI.Api.dll"]
