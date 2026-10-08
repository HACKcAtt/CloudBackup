# CloudBackup

[English version below ↓](#english-version)

Кроссплатформенное приложение для автоматического резервного копирования выбранной папки в **Облако Mail.ru** через WebDAV.

- 🖥 **Windows** — устанавливается как служба Windows, запускается автоматически при загрузке ОС.
- 🐧 **Linux** — работает как консольное приложение с UI (Avalonia), служба настраивается вручную через `systemd`.
- 🎨 **GUI** — Avalonia UI, единый код на C#/.NET 8, локализация RU/EN.
- 🔒 **Пароль** хранится отдельно в `secrets.json` с правами `600` на Linux.

---

## Требования

### Для сборки
- **.NET 8 SDK** или новее: https://dotnet.microsoft.com/download
- **Rider** / **Visual Studio 2022** / **VS Code** (опционально)
- **Avalonia Templates**:
  ```bash
  dotnet new install Avalonia.Templates
  ```

### Для работы
- Аккаунт в **Облаке Mail.ru**.
- **Пароль для внешнего приложения** — создаётся в настройках Mail.ru (https://help.mail.ru/cloud/desktop/webdav/).
  - ⚠️ Основной пароль от почты **не подойдёт** — только пароль для внешнего приложения.

---

## Сборка

### Debug-сборка (для разработки)
```bash
dotnet build
dotnet run --project CloudBackup.App
```

### Release-сборка под Windows (single-file exe)
```bash
dotnet publish CloudBackup.App/CloudBackup.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o ./publish/win-x64
```

Готовый `.exe` появится в `./publish/win-x64/CloudBackup.App.exe`. Никаких зависимостей ставить не нужно — .NET включён внутрь.

### Release-сборка под Linux
```bash
dotnet publish CloudBackup.App/CloudBackup.App.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o ./publish/linux-x64
```

---

## Использование

1. Запусти приложение: `CloudBackup.App.exe` (Windows) или `./CloudBackup.App` (Linux).
2. Введи **Email** от Mail.ru и **пароль для внешнего приложения**.
3. Выбери **папку** для бэкапа.
4. Выбери **расписание** (1 минута — 30 дней).
5. Нажми **«Проверить соединение»** — приложение убедится, что пароль верный и покажет свободное место в облаке.
6. Нажми **«Установить и запустить службу»**:
   - На **Windows** появится окно UAC, приложение установится как служба и запустится автоматически.
   - На **Linux** настройки сохранятся, службу нужно настроить через `systemd` (см. ниже).
7. Закрой окно приложения — служба продолжит работать в фоне.

---

## Настройка на Linux (systemd)

После того как приложение сохранило конфиг в `~/.config/CloudBackup/`, создай unit-файл:

```ini
# /etc/systemd/system/cloudbackup.service
[Unit]
Description=Cloud Mail.ru Backup Service
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=YOUR_USERNAME
WorkingDirectory=/opt/cloudbackup
ExecStart=/opt/cloudbackup/CloudBackup.App
Restart=on-failure
RestartSec=30
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
```

Затем:
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now cloudbackup
sudo systemctl status cloudbackup
```

Проверить логи:
```bash
journalctl -u cloudbackup -f
```

---

## Где хранятся настройки

Приложение хранит настройки в двух файлах:

| Файл | Содержимое | Безопасность |
|---|---|---|
| `app-settings.json` | Папка, расписание, буква диска | Публичный — можно шарить |
| `secrets.json` | Email, пароль для внешнего приложения | **Приватный** — не коммитить |

**Расположение:**
- **Windows:** `%ProgramData%\CloudBackup\`
- **Linux:** `~/.config/CloudBackup/`

На Linux `secrets.json` создаётся с правами `600` (только владелец может читать).

### Шаблоны
В корне репозитория лежат `*.example.json` — структура без реальных данных. Скопируй и заполни при необходимости:

```bash
cp app-settings.example.json ~/.config/CloudBackup/app-settings.json
cp secrets.example.json ~/.config/CloudBackup/secrets.json
chmod 600 ~/.config/CloudBackup/secrets.json
```

---

## Логи

Логи пишутся в:
- **Windows:** `%ProgramData%\CloudBackup\logs\`
- **Linux:** `~/.config/CloudBackup/logs/`

Файл `backup-YYYYMMDD.log`, ротация ежедневная, хранится 14 дней.

---

## Структура проекта

```
CloudBackup/
├── CloudBackup.App/           # Avalonia UI
│   ├── ViewModels/            # MVVM ViewModels
│   ├── Views/                 # XAML-разметка
│   ├── Services/              # LocalizationService, DialogService
│   ├── Resources/             # .resx-файлы локализации
│   ├── Program.cs             # Точка входа (UI + Service)
│   ├── App.axaml              # Application styles
│   └── CloudBackup.App.csproj
│
├── CloudBackup.Core/          # Бизнес-логика (без UI)
│   ├── Models/                # AppConfig, WebDavSettings
│   ├── Services/              # WebDavService, BackupService, ServiceInstaller
│   └── CloudBackup.Core.csproj
│
├── app-settings.example.json
├── secrets.example.json
├── .gitignore
└── README.md
```

---

## Лицензия

MIT — используй свободно.

---

## Известные ограничения

- ⚠️ При загрузке больших файлов через WebDAV индикатор на клиенте может «застрять» на 99%. Это специфика протокола — файл докачивается в фоне, дождись завершения.
- ⚠️ Бэкап **не версионируется** — если файл с тем же именем изменился, он перезаписывается.
- ⚠️ На Linux для монтирования WebDAV нужен пакет `davfs2`: `sudo apt install davfs2`.

---
---

# English version

[↑ Наверх / Back to top](#cloudbackup)

Cross-platform application for automatic backup of a selected folder to **Mail.ru Cloud** via WebDAV.

- 🖥 **Windows** — installs itself as a Windows Service, starts automatically on OS boot.
- 🐧 **Linux** — runs as a GUI application (Avalonia), service is configured manually via `systemd`.
- 🎨 **GUI** — Avalonia UI, single C#/.NET 8 codebase, RU/EN localization.
- 🔒 **Password** is stored separately in `secrets.json` with `600` permissions on Linux.

---

## Requirements

### For building
- **.NET 8 SDK** or newer: https://dotnet.microsoft.com/download
- **Rider** / **Visual Studio 2022** / **VS Code** (optional)
- **Avalonia Templates**:
  ```bash
  dotnet new install Avalonia.Templates
  ```

### For running
- A **Mail.ru Cloud** account.
- An **external app password** — created in Mail.ru settings (https://help.mail.ru/cloud/desktop/webdav/).
  - ⚠️ Your main email password **will not work** — only the external app password.

---

## Build

### Debug build (for development)
```bash
dotnet build
dotnet run --project CloudBackup.App
```

### Release build for Windows (single-file exe)
```bash
dotnet publish CloudBackup.App/CloudBackup.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o ./publish/win-x64
```

The resulting `.exe` will be at `./publish/win-x64/CloudBackup.App.exe`. No dependencies need to be installed — .NET is bundled inside.

### Release build for Linux
```bash
dotnet publish CloudBackup.App/CloudBackup.App.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o ./publish/linux-x64
```

---

## Usage

1. Launch the app: `CloudBackup.App.exe` (Windows) or `./CloudBackup.App` (Linux).
2. Enter your **Mail.ru email** and **external app password**.
3. Choose the **folder** to back up.
4. Choose a **schedule** (1 minute — 30 days).
5. Click **"Test connection"** — the app will verify the password and show free cloud space.
6. Click **"Install and start service"**:
   - On **Windows**, a UAC prompt will appear; the app installs itself as a service and starts automatically.
   - On **Linux**, settings are saved; you need to configure the service via `systemd` (see below).
7. Close the app window — the service keeps running in the background.

---

## Linux setup (systemd)

After the app has saved its config to `~/.config/CloudBackup/`, create a unit file:

```ini
# /etc/systemd/system/cloudbackup.service
[Unit]
Description=Cloud Mail.ru Backup Service
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=YOUR_USERNAME
WorkingDirectory=/opt/cloudbackup
ExecStart=/opt/cloudbackup/CloudBackup.App
Restart=on-failure
RestartSec=30
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
```

Then:
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now cloudbackup
sudo systemctl status cloudbackup
```

View logs:
```bash
journalctl -u cloudbackup -f
```

---

## Where settings are stored

The app stores settings in two files:

| File | Contents | Security |
|---|---|---|
| `app-settings.json` | Folder, schedule, drive letter | Public — safe to share |
| `secrets.json` | Email, external app password | **Private** — do not commit |

**Location:**
- **Windows:** `%ProgramData%\CloudBackup\`
- **Linux:** `~/.config/CloudBackup/`

On Linux, `secrets.json` is created with `600` permissions (owner-only read).

### Templates
The repository root contains `*.example.json` — structure without real data. Copy and fill in if needed:

```bash
cp app-settings.example.json ~/.config/CloudBackup/app-settings.json
cp secrets.example.json ~/.config/CloudBackup/secrets.json
chmod 600 ~/.config/CloudBackup/secrets.json
```

---

## Logs

Logs are written to:
- **Windows:** `%ProgramData%\CloudBackup\logs\`
- **Linux:** `~/.config/CloudBackup/logs/`

File `backup-YYYYMMDD.log`, daily rotation, kept for 14 days.

---

## Project structure

```
CloudBackup/
├── CloudBackup.App/           # Avalonia UI
│   ├── ViewModels/            # MVVM ViewModels
│   ├── Views/                 # XAML markup
│   ├── Services/              # LocalizationService, DialogService
│   ├── Resources/             # Localization .resx files
│   ├── Program.cs             # Entry point (UI + Service)
│   ├── App.axaml              # Application styles
│   └── CloudBackup.App.csproj
│
├── CloudBackup.Core/          # Business logic (no UI)
│   ├── Models/                # AppConfig, WebDavSettings
│   ├── Services/              # WebDavService, BackupService, ServiceInstaller
│   └── CloudBackup.Core.csproj
│
├── app-settings.example.json
├── secrets.example.json
├── .gitignore
└── README.md
```

---

## License

MIT — free to use.

---

## Known limitations

- ⚠️ When uploading large files via WebDAV, the client-side progress bar may "freeze" at 99%. This is a protocol quirk — the file is still being uploaded, just wait for it to complete.
- ⚠️ Backups are **not versioned** — if a file with the same name changes, it is overwritten.
- ⚠️ On Linux, the `davfs2` package is required to mount WebDAV: `sudo apt install davfs2`.
