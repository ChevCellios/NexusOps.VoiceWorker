FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY NexusOps.VoiceWorker.csproj ./
COPY NexusOps.Web/NexusOps.Web.csproj NexusOps.Web/
RUN dotnet restore NexusOps.VoiceWorker.csproj

COPY . ./
RUN dotnet publish NexusOps.VoiceWorker.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Use Microsoft's minimal, non-root runtime image. The "extra" variant retains
# ICU and time-zone data required for Croatian locale formatting.
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "NexusOps.VoiceWorker.dll"]
