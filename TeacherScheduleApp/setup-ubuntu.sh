#!/usr/bin/env bash
# setup-ubuntu.sh — instalace závislostí pro Ubuntu 18.04+
set -euo pipefail
# 1) must be root
if [[ "$(id -u)" -ne 0 ]]; then
  echo "Tento skript je třeba spustit jako root:"
  echo "  sudo $0"
  exit 1
fi
# 2) must be Ubuntu
. /etc/os-release
if [[ "$ID" != "ubuntu" ]]; then
  echo "Chyba: tento skript je určen pouze pro Ubuntu."
  exit 1
fi
UBU_VER="$VERSION_ID"       # e.g. "24.04"
echo "==> Zjištěno Ubuntu $VERSION ($UBU_VER)"

echo "==> Aktualizuji seznam balíčků…"
apt-get update

echo "==> Instalace základních nástrojů…"
apt-get install -y \
    apt-transport-https \
    ca-certificates \
    gnupg \
    lsb-release \
    wget \
    curl
echo "==> Přidávám Microsoft repozitář pro .NET…"
URL="https://packages.microsoft.com/config/ubuntu/${UBU_VER}/packages-microsoft-prod.deb"
wget -q "$URL" -O /tmp/packages-microsoft-prod.deb \
  && dpkg -i /tmp/packages-microsoft-prod.deb \
  && rm /tmp/packages-microsoft-prod.deb
echo "==> Aktualizuji seznam balíčků…"
apt-get update
echo "==> Instalace .NET SDK 8.0 (včetně CLI)…"
apt-get install -y dotnet-sdk-8.0
echo "==> Instalace závislostí pro Avalonia GUI…"
apt-get install -y \
    libgtk-3-0 \
    libgtk-4-1 \
    libgdk-pixbuf2.0-0 \
    libxtst6 \
    libxss1 \
    libnss3 \
    libxinerama1 \
    libxcursor1 \
    libxrandr2 \
    libfontconfig1 \
    libfreetype6
echo "==> Instalace Ghostscript pro náhled PDF…"
apt-get install -y ghostscript libgs-dev
echo "==> Instalace libgdiplus pro System.Drawing.Common…"
apt-get install -y libgdiplus
echo "==> Instalace SQLite…"
apt-get install -y sqlite3 libsqlite3-dev
echo
echo "✔ Všechny závislosti byly nainstalovány!"
echo "   Nyní můžete:"
echo "     dotnet --info"
echo "     dotnet build"
echo "     dotnet run --project TeacherScheduleApp/TeacherScheduleApp.csproj"