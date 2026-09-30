# Build from the repo root: docker build -f src/Claimline.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Claimline.Contracts/*.csproj src/Claimline.Contracts/
COPY src/Claimline.ServiceDefaults/*.csproj src/Claimline.ServiceDefaults/
COPY src/Claimline.Api/*.csproj src/Claimline.Api/
RUN dotnet restore src/Claimline.Api/Claimline.Api.csproj
COPY src/ src/
RUN dotnet publish src/Claimline.Api/Claimline.Api.csproj -c Release -o /app --no-restore

# Chiseled: no shell or package manager, non-root by default. "-extra" adds ICU, which SqlClient requires.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Claimline.Api.dll"]
