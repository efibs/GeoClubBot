# GeoClubBot
TODO

# Start the database

```bash
docker compose up postgresqldb
```

# Building and Publishing

## Automated (CI/CD)

Publishing is automated via GitHub Actions (see [`.github/workflows/`](.github/workflows) and
the [branching model in CONTRIBUTING.md](CONTRIBUTING.md#branching-model)):

- **Release** — push a SemVer tag (no `v` prefix, e.g. `0.13.0`). The tagged commit is fully
  tested, then the image is built and pushed as `ghcr.io/efibs/geo-club-bot:0.13.0`, `:0.13`,
  `:0` and `:latest`, and a GitHub Release is created.
  ```bash
  git tag 0.13.0 && git push origin 0.13.0
  ```
- **Dev/staging** — every push to `dev` publishes a rolling `ghcr.io/efibs/geo-club-bot:dev`
  image (plus `:dev-<sha>`).

Both run the full test suite first and authenticate to GHCR with the built-in `GITHUB_TOKEN`
(no `CR_PAT` needed in CI).

## Manual

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

Start vLLM with gpt-oss-20b or qwen2.5-14B-Instruct:
```bash
docker run -it --rm \
  --runtime nvidia --gpus all \
  --ipc=host \
  -p 8002:8000 \
  -v ~/.cache/huggingface:/root/.cache/huggingface \
  vllm/vllm-openai:latest \
  --model openai/gpt-oss-20b \
  --host 0.0.0.0 \
  --gpu-memory-utilization 0.8 \
  --tool-call-parser openai \
  --enable-auto-tool-choice
  
docker run -it --rm \
  --runtime nvidia --gpus all \
  --ipc=host \
  -p 8002:8000 \
  -v ~/.cache/huggingface:/root/.cache/huggingface \
  vllm/vllm-openai:latest \
  --model Qwen/Qwen2.5-14B-Instruct \
  --quantization bitsandbytes \
  --dtype auto \
  --gpu-memory-utilization 0.8 \
  --host 0.0.0.0 \
  --enable-auto-tool-choice \
  --tool-call-parser hermes
```

Start llama-3:
```bash
docker run -it --rm --runtime nvidia --gpus all --ipc=host -p 8002:8000 -v ~/.cache/huggingface:/root/.cache/huggingface vllm/vllm-openai:latest --model meta-llama/Llama-3.1-8B-Instruct --host 0.0.0.0 --gpu-memory-utilization 0.8 --tool-call-parser openai --enable-auto-tool-choice --max-model-len 1024
```

run embedding model:
```bash
docker run -it --rm --runtime nvidia --gpus all --ipc=host -p 8001:8000 -v ~/.cache/huggingface:/root/.cache/huggingface vllm/vllm-openai:latest --model BAAI/bge-large-en-v1.5 --gpu-memory-utilization 0.05 --max-num-seqs 1
```

run Qwen3 on GTX 1650:
```bash
docker run -it --rm --runtime nvidia --gpus all --ipc=host -p 8002:8000 -v ~/.cache/huggingface:/root/.cache/huggingface -e VLLM_USE_FLASHINFER_SAMPLER=0 vllm/vllm-openai:latest --model Qwen/Qwen3-1.7B --gpu-memory-utilization 0.7 --max-model-len 8192 --host 0.0.0.0 --quantization bitsandbytes --max-num-seqs 1 --enable-auto-tool-choice --tool-call-parser hermes
```