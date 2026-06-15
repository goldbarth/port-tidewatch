# Build context: repository root (needs Contracts + Source).
#   docker build -f src/Tidewatch.Source/Dockerfile -t tidewatch-source:dev .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Tidewatch.Contracts/Tidewatch.Contracts.csproj src/Tidewatch.Contracts/
COPY src/Tidewatch.Source/Tidewatch.Source.csproj src/Tidewatch.Source/
RUN dotnet restore src/Tidewatch.Source/Tidewatch.Source.csproj

COPY src/Tidewatch.Contracts/ src/Tidewatch.Contracts/
COPY src/Tidewatch.Source/ src/Tidewatch.Source/
RUN dotnet publish src/Tidewatch.Source/Tidewatch.Source.csproj -c Release -o /app --no-restore

# Console producer: plain runtime is enough (no ASP.NET Core).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Tidewatch.Source.dll"]
