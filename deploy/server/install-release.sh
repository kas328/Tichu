#!/usr/bin/env bash
# 서버 쪽 릴리스 설치(M2b). deploy-server.ps1 이 sudo 로 부른다.
# tgz 를 releases/<이름> 에 풀고 current 를 바꾼 뒤 재시작한다. 30초 안에 7777 을 듣지 않으면
# 직전 릴리스로 되돌리고 exit 1. 성공하면 현재·직전 릴리스만 남긴다.
set -euo pipefail
tgz="${1:?tgz 경로}"
base=/opt/tichu
name="$(basename "$tgz" .tgz)"
dest="$base/releases/$name"

prev="$(readlink -f "$base/current" 2>/dev/null || true)"
rm -rf "$dest"
mkdir -p "$dest"
tar -xzf "$tgz" -C "$dest"
chmod +x "$dest/Tichu.Server"
chown -R tichu:tichu "$dest"
ln -sfn "$dest" "$base/current"
systemctl restart tichu-server

healthy() {
  for _ in $(seq 1 30); do
    if systemctl is-active --quiet tichu-server && ss -ltnH 'sport = :7777' | grep -q .; then return 0; fi
    sleep 1
  done
  return 1
}

if healthy; then
  for d in "$base"/releases/*; do
    if [ "$d" != "$dest" ] && [ "$d" != "$prev" ]; then rm -rf "$d"; fi
  done
  rm -f "$tgz"
  echo "설치 완료: $name"
  exit 0
fi

echo "기동 확인 실패: $name" >&2
journalctl -u tichu-server -n 30 --no-pager >&2 || true
if [ -n "$prev" ] && [ -d "$prev" ] && [ "$prev" != "$dest" ]; then
  ln -sfn "$prev" "$base/current"
  systemctl restart tichu-server
  echo "직전 릴리스로 되돌림: $(basename "$prev")" >&2
fi
exit 1
