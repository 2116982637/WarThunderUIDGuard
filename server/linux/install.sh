#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
stage="${1:-$script_directory}"
admin_stage="$stage"
refresh_stage="$stage"
www_root="/srv/wtuidguard/www"
state_root="/var/lib/wtuidguard"
secret_root="/etc/wtuidguard/secrets"
install_root="/opt/wtuidguard"

required=(
  "$admin_stage/admin_upload_server.py"
  "$admin_stage/wtuidguard_common.py"
  "$admin_stage/nginx.conf"
  "$admin_stage/wtuidguard-admin-upload.service"
  "$refresh_stage/refresh_content.py"
  "$refresh_stage/warthunder-uid-guard-refresh.service"
  "$refresh_stage/warthunder-uid-guard-refresh.timer"
  "$www_root/blacklist.json"
  "$www_root/blacklist.sig"
  "$www_root/signing-public.xml"
  "$www_root/updates/latest.json"
  "$www_root/updates/latest.sig"
  "$secret_root/admin-upload-hmac.key"
  "$secret_root/signing-private.pem"
)

for path in "${required[@]}"; do
  if [[ ! -f "$path" || -L "$path" ]]; then
    echo "Required regular file is missing or unsafe: $path" >&2
    exit 1
  fi
done

if [[ "$(stat -c '%s' "$secret_root/admin-upload-hmac.key")" != "32" ]]; then
  echo "Administrator HMAC key must contain exactly 32 raw bytes." >&2
  exit 1
fi

id wtuidguard >/dev/null
command -v python3 >/dev/null
command -v openssl >/dev/null
command -v nginx >/dev/null

openssl pkey -in "$secret_root/signing-private.pem" -check -noout >/dev/null

python3 -m py_compile \
  "$admin_stage/admin_upload_server.py" \
  "$admin_stage/wtuidguard_common.py" \
  "$refresh_stage/refresh_content.py"

timestamp="$(date -u +%Y%m%d-%H%M%S)"
backup_root="$state_root/deploy-backups/$timestamp"
install -d -o wtuidguard -g wtuidguard -m 0750 "$backup_root"

for current in \
  /etc/nginx/conf.d/warthunder-uid-guard.conf \
  /etc/systemd/system/wtuidguard-admin-upload.service \
  /etc/systemd/system/warthunder-uid-guard-refresh.service \
  /etc/systemd/system/warthunder-uid-guard-refresh.timer; do
  if [[ -f "$current" && ! -L "$current" ]]; then
    cp -a -- "$current" "$backup_root/"
  fi
done

install -d -o root -g root -m 0755 "$install_root"
install -o root -g root -m 0644 \
  "$admin_stage/admin_upload_server.py" "$install_root/admin_upload_server.py"
install -o root -g root -m 0644 \
  "$admin_stage/wtuidguard_common.py" "$install_root/wtuidguard_common.py"
install -o root -g root -m 0644 \
  "$refresh_stage/refresh_content.py" "$install_root/refresh_content.py"

install -o root -g root -m 0644 \
  "$admin_stage/nginx.conf" /etc/nginx/conf.d/warthunder-uid-guard.conf
install -o root -g root -m 0644 \
  "$admin_stage/wtuidguard-admin-upload.service" \
  /etc/systemd/system/wtuidguard-admin-upload.service
install -o root -g root -m 0644 \
  "$refresh_stage/warthunder-uid-guard-refresh.service" \
  /etc/systemd/system/warthunder-uid-guard-refresh.service
install -o root -g root -m 0644 \
  "$refresh_stage/warthunder-uid-guard-refresh.timer" \
  /etc/systemd/system/warthunder-uid-guard-refresh.timer

install -d -o wtuidguard -g wtuidguard -m 0755 "$www_root" "$www_root/updates"
install -d -o wtuidguard -g wtuidguard -m 0750 \
  "$state_root" "$state_root/upload-backups"
install -d -o root -g wtuidguard -m 0750 "$secret_root"
chown root:wtuidguard \
  "$secret_root/admin-upload-hmac.key" "$secret_root/signing-private.pem"
chmod 0640 \
  "$secret_root/admin-upload-hmac.key" "$secret_root/signing-private.pem"

# Everything below the web root is public content. It must be readable by nginx,
# while the private keys above remain inaccessible to the nginx account.
find "$www_root" -xdev -type d -exec chmod 0755 {} +
find "$www_root" -xdev -type f -exec chmod 0644 {} +
chown -R wtuidguard:wtuidguard "$www_root"

python3 - <<'PY'
import importlib.util
from pathlib import Path

module_path = Path('/opt/wtuidguard/wtuidguard_common.py')
spec = importlib.util.spec_from_file_location('wtuidguard_common', module_path)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
spec.loader.exec_module(module)
module.parse_and_validate_blacklist(Path('/srv/wtuidguard/www/blacklist.json').read_bytes())
PY

nginx -t
systemctl daemon-reload
systemctl enable --now wtuidguard-admin-upload.service
systemctl enable --now warthunder-uid-guard-refresh.timer
systemctl enable --now nginx

systemctl is-active --quiet wtuidguard-admin-upload.service
systemctl is-active --quiet warthunder-uid-guard-refresh.timer
systemctl is-active --quiet nginx

cat >"$state_root/deploy-result.txt" <<EOF
installed_at=$timestamp
admin_service=active
refresh_timer=active
nginx=active
EOF
chown wtuidguard:wtuidguard "$state_root/deploy-result.txt"
chmod 0640 "$state_root/deploy-result.txt"

echo "INSTALL_OK $timestamp"
