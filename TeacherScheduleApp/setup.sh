#!/usr/bin/env bash
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Tento skript musíte spustit jako root:"
  echo "  sudo ./setup.sh"
  exit 1
fi

echo "=== Aktualizuji index balíčků ==="
apt-get update

echo "=== Instalace základních nástrojů ==="
apt-get install -y \
    apt-transport-https \
    ca-certificates \
    gnupg \
    wget

echo "=== Přidávám Microsoft repozitář pro .NET 8.0 ==="
wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb \
     -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
rm /tmp/packages-microsoft-prod.deb

echo "=== Instalace .NET Runtime 8.0 ==="
apt-get update
apt-get install -y dotnet-runtime-8.0

echo "=== Instalace závislostí pro Avalonia GUI ==="
apt-get install -y \
    libgtk-3-0 \
    libgtk-4-1 \
    libgdk-pixbuf2.0-0 \
    libxtst6 \
    libxss1 \
    libnss3 \
    libasound2 \
    libxinerama1 \
    libxcursor1 \
    libxrandr2

echo "=== Instalace Ghostscript pro náhled PDF ==="
apt-get install -y ghostscript libgs-dev

echo "=== Instalace libgdiplus pro System.Drawing.Common ==="
apt-get install -y libgdiplus

echo "=== Instalace SQLite, pokud chybí ==="
apt-get install -y sqlite3 libsqlite3-dev

echo
echo "✔ Všechny závislosti byly úspěšně nainstalovány!"
echo "Nyní můžete aplikaci sestavit a spustit:"
echo "  dotnet build"
echo "  dotnet run"