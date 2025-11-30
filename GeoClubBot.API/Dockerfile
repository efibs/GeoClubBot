FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
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

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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

FROM base AS final

WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoClubBot.API.dll"]
