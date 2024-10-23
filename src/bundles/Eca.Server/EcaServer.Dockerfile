FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /source

# copy sources.
COPY ElsaServer/. ./ElsaServer
# COPY ./NuGet.Config ./
# COPY *.props ./

# restore packages.
RUN dotnet restore "./ElsaServer/ElsaServer.csproj"

# build and publish (UseAppHost=false creates platform independent binaries).
WORKDIR /source/ElsaServer
RUN dotnet build "ElsaServer.csproj" -c Release -o /app/build
RUN dotnet publish "ElsaServer.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore -f net8.0

# move binaries into smaller base image.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS base
WORKDIR /app
COPY --from=build /app/publish ./

# Install Python 3.11
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3.11 \
    python3.11-dev \
    libpython3.11 \
    python3-pip && \
    rm -rf /var/lib/apt/lists/*

# Set PYTHONNET_PYDLL environment variable
ENV PYTHONNET_PYDLL=/usr/lib/aarch64-linux-gnu/libpython3.11.so

EXPOSE 8080/tcp
ENTRYPOINT ["dotnet", "ElsaServer.dll"]
