# GeoClubBot
TODO

# Building and Publishing

To build the docker image, run:
```bash
docker build -f ./GeoClubBot.API/Dockerfile -t ghcr.io/efibs/geo-club-bot:your-version .
```

To publish the docker image to the container registry, run:
```bash
export CR_PAT=YourPersonalAccessToken
echo $CR_PAT | docker login ghcr.io -u USERNAME --password-stdin
docker push ghcr.io/efibs/geo-club-bot:your-version
```