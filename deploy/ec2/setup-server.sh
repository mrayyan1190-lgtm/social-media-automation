#!/usr/bin/env bash
# One-time EC2 (Ubuntu 24.04) setup: swap, nginx, TLS, systemd units for the API and the worker.
# Usage on the server:  sudo bash setup-server.sh <your-domain> <email-for-letsencrypt>
# Then fill /etc/sma.env and run deploy/ec2/publish.sh from your PC.
set -euo pipefail
DOMAIN=${1:?usage: sudo bash setup-server.sh <domain> <email>}
EMAIL=${2:?usage: sudo bash setup-server.sh <domain> <email>}

# t3.micro has 1 GB RAM; API + worker need headroom.
if [ ! -f /swapfile ]; then
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

apt-get update
apt-get install -y nginx certbot python3-certbot-nginx libicu74

id sma >/dev/null 2>&1 || useradd --system --home /var/lib/sma --shell /usr/sbin/nologin sma
# Media bytes and the DataProtection key ring (encrypts stored LLM keys) must survive redeploys; back this folder up.
mkdir -p /opt/sma /var/lib/sma/media /var/lib/sma/keys
chown -R sma:sma /var/lib/sma

if [ ! -f /etc/sma.env ]; then
  install -m 600 /dev/null /etc/sma.env
  echo "Created empty /etc/sma.env, fill it from deploy/ec2/sma.env.example"
fi

for svc in api worker; do
  name=$([ "$svc" = api ] && echo Api || echo Worker)
  cat > /etc/systemd/system/sma-$svc.service <<EOF
[Unit]
Description=Social Media Automation $name
After=network-online.target
Wants=network-online.target

[Service]
User=sma
WorkingDirectory=/opt/sma/$svc
ExecStart=/opt/sma/$svc/SocialMediaAutomation.$name
EnvironmentFile=/etc/sma.env
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
done
systemctl daemon-reload
systemctl enable sma-api sma-worker

cat > /etc/nginx/sites-available/sma <<EOF
server {
    listen 80;
    server_name $DOMAIN;
    client_max_body_size 25m;

    root /opt/sma/web;
    index index.html;

    location /api/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host \$host;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    # Angular client-side routes
    location / {
        try_files \$uri \$uri/ /index.html;
    }
    # index.html names the hashed bundles: always revalidate it so a deploy reaches open browsers on the next load.
    location = /index.html {
        add_header Cache-Control "no-cache";
    }
}
EOF
ln -sf /etc/nginx/sites-available/sma /etc/nginx/sites-enabled/sma
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

# The session cookie is Secure in Production, so the site must be served over HTTPS.
certbot --nginx -d "$DOMAIN" -m "$EMAIL" --agree-tos --non-interactive --redirect
echo "Done. Fill /etc/sma.env, then run publish.sh from your PC."
