FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER 1654
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GeoClubBot.API/GeoClubBot.API.csproj", "GeoClubBot.API/"]
COPY ["Constants/Constants.csproj", "Constants/"]
COPY ["Extensions/Extensions.csproj", "Extensions/"]
COPY ["QuartzExtensions/QuartzExtensions.csproj", "QuartzExtensions/"]
COPY ["Utilities/Utilities.csproj", "Utilities/"]
COPY ["GeoClubBot.Domain/GeoClubBot.Domain.csproj", "GeoClubBot.Domain/"]
COPY ["GeoClubBot.Infrastructure/GeoClubBot.Infrastructure.csproj", "GeoClubBot.Infrastructure/"]
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
