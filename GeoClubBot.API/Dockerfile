# Base images pinned by digest for reproducible, tamper-evident builds.
# The :10.0 tag is kept in the comment for readability; Dependabot (docker ecosystem)
# raises PRs to bump these digests.
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:ddcf70ad1ab963a4fcd41fbd722a6b660e404e87567cfbd46fd2809c21b02088 AS base
RUN apt-get update && apt-get install -y curl libfontconfig1 libfreetype6 libpng16-16 libjpeg-turbo8 libgif7 libglu1-mesa && rm -rf /var/lib/apt/lists/*
# Install required dependencies for Chromium
RUN apt-get update && apt-get install -y \
    wget gnupg ca-certificates fonts-liberation libappindicator3-1 libasound2t64 \
    libatk-bridge2.0-0 libatk1.0-0 libc6 libcairo2 libcups2 libdbus-1-3 \
    libexpat1 libgbm1 libglib2.0-0 libgtk-3-0 \
    libnspr4 libnss3 libpango-1.0-0 libpangocairo-1.0-0 libx11-6 libx11-xcb1 \
    libxcb1 libxcomposite1 libxdamage1 libxext6 libxfixes3 libxrandr2 \
    libxrender1 libxshmfence1 lsb-release xdg-utils \
    && rm -rf /var/lib/apt/lists/*
USER 1654
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build the Vue activity frontend; its static assets are bundled into the API's wwwroot below.
# The Discord client id is deliberately NOT baked in here — the frontend fetches it from the backend
# at runtime (see ActivityController.GetConfig) so this image stays generic and isn't locked to one
# Discord application. The .dockerignore excludes all .env files, keeping the build hermetic.
FROM node:26-alpine@sha256:a2dc166a387cc6ca1e62d0c8e265e49ca985d6e60abc9fe6e6c3d6ce8e63f606 AS frontend
WORKDIR /activity
COPY GeoClubBot.Activity/package.json GeoClubBot.Activity/package-lock.json ./
RUN npm ci
COPY GeoClubBot.Activity/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:548d93f8a18a1acbe6cc127bc4f47281430d34a9e35c18afa80a8d6741c2adc3 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GeoClubBot.API/GeoClubBot.API.csproj", "GeoClubBot.API/"]
COPY ["Constants/Constants.csproj", "Constants/"]
COPY ["Configuration/Configuration.csproj", "Configuration/"]
COPY ["Extensions/Extensions.csproj", "Extensions/"]
COPY ["QuartzExtensions/QuartzExtensions.csproj", "QuartzExtensions/"]
COPY ["Utilities/Utilities.csproj", "Utilities/"]
COPY ["GeoClubBot.Domain/GeoClubBot.Domain.csproj", "GeoClubBot.Domain/"]
COPY ["GeoClubBot.Infrastructure/GeoClubBot.Infrastructure.csproj", "GeoClubBot.Infrastructure/"]
COPY ["GeoClubBot.Discord/GeoClubBot.Discord.csproj", "GeoClubBot.Discord/"]
COPY ["GeoClubBot.Application/GeoClubBot.Application.csproj", "GeoClubBot.Application/"]
RUN dotnet restore "GeoClubBot.API/GeoClubBot.API.csproj"
COPY . .
WORKDIR "/src/GeoClubBot.API"
RUN dotnet build "./GeoClubBot.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GeoClubBot.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
# Bundle the built activity frontend so the API can serve it from wwwroot.
COPY --from=frontend /activity/dist /app/publish/wwwroot

FROM base AS final

WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoClubBot.API.dll"]
