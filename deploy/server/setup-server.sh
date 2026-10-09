#!/usr/bin/env bash
# Lightsail 서버 1회 세팅(M2b 스펙 §4). 여러 번 돌려도 같은 결과(멱등).
# 사용: sudo bash setup-server.sh <도메인>
#   같은 폴더에 tichu-server.service · server.env.default · Caddyfile.template · install-release.sh 가 있어야 한다.
set -euo pipefail
domain="${1:?도메인을 인자로 주세요 (예: play.example.com)}"
here="$(cd "$(dirname "$0")" && pwd)"

export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get install -y -q caddy libicu74   # libicu74: .NET 세계화(self-contained 도 시스템 ICU 를 쓴다)

id tichu >/dev/null 2>&1 || useradd --system --home-dir /opt/tichu --shell /usr/sbin/nologin tichu
install -d -o tichu -g tichu /opt/tichu /opt/tichu/releases
install -d /opt/tichu/bin /etc/tichu
install -m 755 "$here/install-release.sh" /opt/tichu/bin/install-release.sh
install -m 644 "$here/tichu-server.service" /etc/systemd/system/tichu-server.service
# 게임 인자는 운영 중에 고칠 수 있으므로 이미 있으면 덮어쓰지 않는다
[ -f /etc/tichu/server.env ] || install -m 644 "$here/server.env.default" /etc/tichu/server.env
sed "s/__DOMAIN__/$domain/g" "$here/Caddyfile.template" > /etc/caddy/Caddyfile

systemctl daemon-reload
systemctl enable tichu-server   # 첫 배포(install-release.sh)가 시작한다
systemctl enable caddy
systemctl reload-or-restart caddy
echo "세팅 완료: $domain — 다음은 PC 에서 tools/deploy-server.ps1"
