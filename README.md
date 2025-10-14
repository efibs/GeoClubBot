# GeoClubBot
TODO

# Start the database

```bash
docker compose up postgresqldb
```

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

Start vLLM:
```bash
docker run -it --rm \
  --runtime nvidia --gpus all \
  --ipc=host \
  -p 8000:8000 \
  -v ~/.cache/huggingface:/root/.cache/huggingface \
  vllm/vllm-openai:latest \
  --model openai/gpt-oss-20b \
  --host 0.0.0.0 \
  --gpu-memory-utilization 0.8
```