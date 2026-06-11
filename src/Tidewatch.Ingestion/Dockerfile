# Build context: repository root (needs Contracts + Ingestion).
#   docker build -f src/Tidewatch.Ingestion/Dockerfile -t tidewatch-ingestion:dev .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against just the project files first, so the layer caches across code changes.
COPY src/Tidewatch.Contracts/Tidewatch.Contracts.csproj src/Tidewatch.Contracts/
COPY src/Tidewatch.Ingestion/Tidewatch.Ingestion.csproj src/Tidewatch.Ingestion/
RUN dotnet restore src/Tidewatch.Ingestion/Tidewatch.Ingestion.csproj

COPY src/Tidewatch.Contracts/ src/Tidewatch.Contracts/
COPY src/Tidewatch.Ingestion/ src/Tidewatch.Ingestion/
RUN dotnet publish src/Tidewatch.Ingestion/Tidewatch.Ingestion.csproj -c Release -o /app --no-restore

# aspnet runtime: the service hosts the dashboard HTTP API alongside the consumer.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tidewatch.Ingestion.dll"]
