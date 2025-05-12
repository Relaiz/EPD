Níže jsou uvedeny kroky, které musí uživatel provést, aby aplikace TeacherScheduleApp fungovala správně na Linuxu i na Windows.

Obecné požadavky

Na obou platformách je potřeba mít nainstalovaný Git a připojení k internetu pro stažení repozitáře a balíčků.

Aplikace je postavena na .NET 8.0 SDK (včetně CLI), které je třeba nainstalovat před sestavením.

Pro náhled PDF je potřeba Ghostscript (na Linuxu i Windows).

Spuštění na Linuxu (Ubuntu)

Pro Ubuntu 18.04 a novější proveďte následující kroky:
Alternativa: Pokud máte ve složce soubor setup-ubuntu.sh, můžete ho spustit a automaticky nainstalovat všechny závislosti:

sudo chmod +x setup-ubuntu.sh
sed -i 's/\r$//' setup-ubuntu.sh
sudo ./setup-ubuntu.sh

Aktualizujte seznam balíčků:
Spusťte v terminálu sudo apt update.

Nainstalujte základní nástroje:
Zadejte
sudo apt install -y apt-transport-https ca-certificates gnupg lsb-release wget curl.

Přidejte Microsoft .NET repozitář:
Zjistěte verzi Ubuntu příkazem 

grep '^VERSION_ID=' /etc/os-release | cut -d '"' -f2 a poté stáhněte a nainstalujte balíček repozitáře:

wget -q "https://packages.microsoft.com/config/ubuntu/${dist_ver}/packages-microsoft-prod.deb" -O /tmp/ms-prod.deb

sudo dpkg -i /tmp/ms-prod.deb && rm /tmp/ms-prod.deb

Nainstalujte .NET 8.0 SDK:
Aktualizujte znovu (sudo apt update) a spusťte
sudo apt install -y dotnet-sdk-8.0.

Nainstalujte nativní knihovny pro Avalonia, PDF, databázi:
sudo apt install -y \
    libgtk-3-0 libgtk-4-1 libgdk-pixbuf2.0-0 libxtst6 libxss1 libnss3 libxinerama1 \
    libxcursor1 libxrandr2  libfontconfig1 libfreetype6 \
    ghostscript libgs-dev libgdiplus sqlite3 libsqlite3-dev

Klonování repozitáře a spuštění aplikace:

git clone [https://github.com/Relaiz/EPD.git](https://github.com/Relaiz/EPD.git)

cd TeacherScheduleApp

dotnet build

dotnet run --project TeacherScheduleApp/TeacherScheduleApp.csproj


Windows (PowerShell)

Nainstalujte .NET 8.0 SDK:

Doporučeno přes winget:

winget install --id Microsoft.DotNet.SDK.8 -e

Případně stáhněte z: https://dotnet.microsoft.com/download/dotnet/8.0

Nainstalujte Ghostscript (pro náhled PDF):

Přes winget:

winget install --id Ghostscript.GS -e

Nebo přes Chocolatey:

choco install ghostscript -y

Případně ručně stáhněte ze: https://www.ghostscript.com/download.html

Klonujte repozitář:

git clone [https://github.com/Relaiz/EPD.git](https://github.com/Relaiz/EPD.git)
cd TeacherScheduleApp

Sestavte a spusťte aplikaci:
dotnet build
dotnet run --project TeacherScheduleApp\TeacherScheduleApp.csproj
