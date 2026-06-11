# Build context: repository root (needs Contracts + Simulator).
#   docker build -f src/Tidewatch.Simulator/Dockerfile -t tidewatch-simulator:dev .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Tidewatch.Contracts/Tidewatch.Contracts.csproj src/Tidewatch.Contracts/
COPY src/Tidewatch.Simulator/Tidewatch.Simulator.csproj src/Tidewatch.Simulator/
RUN dotnet restore src/Tidewatch.Simulator/Tidewatch.Simulator.csproj

COPY src/Tidewatch.Contracts/ src/Tidewatch.Contracts/
COPY src/Tidewatch.Simulator/ src/Tidewatch.Simulator/
RUN dotnet publish src/Tidewatch.Simulator/Tidewatch.Simulator.csproj -c Release -o /app --no-restore

# Console producer: plain runtime is enough (no ASP.NET Core).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Tidewatch.Simulator.dll"]
