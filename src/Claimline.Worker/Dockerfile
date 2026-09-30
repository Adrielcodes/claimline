# Build from the repo root: docker build -f src/Claimline.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Claimline.Contracts/*.csproj src/Claimline.Contracts/
COPY src/Claimline.ServiceDefaults/*.csproj src/Claimline.ServiceDefaults/
COPY src/Claimline.Triage/*.csproj src/Claimline.Triage/
COPY src/Claimline.Worker/*.csproj src/Claimline.Worker/
RUN dotnet restore src/Claimline.Worker/Claimline.Worker.csproj
COPY src/ src/
RUN dotnet publish src/Claimline.Worker/Claimline.Worker.csproj -c Release -o /app --no-restore

# The worker hosts ASP.NET Core types via ServiceDefaults, so it needs the aspnet runtime too.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Claimline.Worker.dll"]
