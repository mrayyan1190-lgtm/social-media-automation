#!/usr/bin/env bash
# Build on your PC (the server is too small to build) and ship to EC2.
# Usage from the repo root (Git Bash):  bash deploy/ec2/publish.sh ubuntu@<elastic-ip> path/to/key.pem
set -euo pipefail
HOST=${1:?usage: bash deploy/ec2/publish.sh ubuntu@<ip> <key.pem>}
KEY=${2:?usage: bash deploy/ec2/publish.sh ubuntu@<ip> <key.pem>}

rm -rf out && mkdir -p out
# Self-contained: no .NET runtime needs to be installed on the server.
dotnet publish backend/src/SocialMediaAutomation.Api    -c Release -r linux-x64 --self-contained -o out/api
dotnet publish backend/src/SocialMediaAutomation.Worker -c Release -r linux-x64 --self-contained -o out/worker
(cd frontend && npm ci && npm run build)
cp -r frontend/dist/frontend/browser out/web
tar -czf out/sma.tgz -C out api worker web

scp -i "$KEY" out/sma.tgz "$HOST:/tmp/sma.tgz"
ssh -i "$KEY" "$HOST" 'set -e
  sudo rm -rf /opt/sma/api /opt/sma/worker /opt/sma/web
  sudo tar -xzf /tmp/sma.tgz -C /opt/sma
  sudo chmod +x /opt/sma/api/SocialMediaAutomation.Api /opt/sma/worker/SocialMediaAutomation.Worker
  sudo systemctl restart sma-api sma-worker
  # Startup (migrations, seeding) can take longer than a fixed sleep: poll for up to 60s, then show why it failed.
  for i in $(seq 1 30); do
    if curl -fsS http://127.0.0.1:5000/health/live >/dev/null 2>&1; then echo "API is up"; exit 0; fi
    sleep 2
  done
  echo "API did not answer /health/live within 60s. Recent sma-api logs:"
  sudo journalctl -u sma-api -n 60 --no-pager
  exit 1'
