FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:7.0-bookworm-slim AS build
WORKDIR /source

# copy sources.
COPY ElsaStudio/. ./ElsaStudio
# COPY ./NuGet.Config ./
# COPY *.props ./

# restore packages.
RUN dotnet restore "./ElsaStudio/ElsaStudio.csproj"

# build and publish (UseAppHost=false creates platform independent binaries).
WORKDIR /source/ElsaStudio
RUN dotnet build "ElsaStudio.csproj" -c Release -o /app/build
RUN dotnet publish "ElsaStudio.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore -f net7.0

# move binaries into smaller base image.
FROM mcr.microsoft.com/dotnet/aspnet:7.0-bookworm-slim AS base
WORKDIR /app
COPY --from=build /app/publish ./

EXPOSE 80/tcp
ENTRYPOINT ["dotnet", "ElsaStudio.dll"]
