FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
RUN mkdir /home/app/data && \
    chown -R 1654:1654 /home/app/data
USER 1654
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GeoClubBot/GeoClubBot.csproj", "GeoClubBot/"]
COPY ["Constants/Constants.csproj", "Constants/"]
COPY ["Extensions/Extensions.csproj", "Extensions/"]
COPY ["Utilities/Utilities.csproj", "Utilities/"]
COPY ["Entities/Entities.csproj", "Entities/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
COPY ["UseCases/UseCases.csproj", "UseCases/"]
RUN dotnet restore "GeoClubBot/GeoClubBot.csproj"
COPY . .
WORKDIR "/src/GeoClubBot"
RUN dotnet build "./GeoClubBot.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GeoClubBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoClubBot.dll"]
