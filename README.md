# MMServer Manager v1.0

One-window control panel for the **Kayito 0.97k** offline server on Windows.
It sets everything up for you: portable MariaDB database, IP in the config files, the client
encoder, and starts / stops the servers in the right order.

By **90minutes** · https://www.youtube.com/@90minu93 · MIT License.
With AI's support, I was essentially "vibe coding"—simply checking in every five hours to ask for code edits to build the app, followed by testing (since I’m a software tester by profession).

## Requirements
- Windows 10 or newer (64-bit).
- .NET Framework 4.8 (already included in Windows 10 version 1903 and newer).
- Visual C++ Redistributable 2015-2022, **x86 (32-bit)**: https://aka.ms/vs/17/release/vc_redist.x86.exe
  (the servers need it; **Check System** tells you if it is missing).
  No internet? The repo you downloaded has a copy in `Dependencies\C++ Redistributables 2017\VC_redist.x86.exe`; the Microsoft link is newer and preferred.
- The Kayito server repo, downloaded by you from GitHub: https://github.com/nicomuratona/MuEmu-0.97k-kayito
  (it provides `MuServer`, `Encoder` and `Client`). **This package does not contain any server or client files.**

## Install
1. Download the Kayito repo from GitHub (Code > Download ZIP, or `git clone`) and extract it.
2. Download `MMServerManager-v1.0.zip` from the Releases page and extract it **into the repo folder**
   (the folder that contains `MuServer`, `Client` and `Encoder`), or extract anywhere and copy the files there.
3. Download the **MariaDB ZIP** (Windows, package type "ZIP file", version 10.11) from https://mariadb.org/download
   and put it next to `MMServerManager.exe`. Do not rename it; the file name must start with `mariadb`.
4. Follow the repo's own instructions for the client files you need in `Client\`.

```
<repo folder>\
  MMServerManager.exe    from this release
  mariadb-xx.zip         you download it from mariadb.org (extracted by the manager on first run)
  Assets\                from this release (icon, logo, header; optional)
  Docs\                  from this release
  START-HERE.txt         from this release
  MuServer\  Encoder\  Client\   from the Kayito repo
```

## Quick start
1. Run `MMServerManager.exe`. Check that **Server folder** and **Client folder** point to `MuServer` and `Client`.
2. Type your LAN IP (or press **Detect**).
3. Press **Check System**, then **Start All**. The first run takes a minute (MariaDB is extracted and the database is created).
4. Press **Open Client** and play. Demo accounts: `test1` to `test5` (password = the account name). Change or remove them if others can reach your server.

## Buttons
| Button | What it does |
|---|---|
| Start All | Sets the IP, builds the client if the IP changed, then starts MariaDB, DataServer, JoinServer, ConnectServer, GameServer. It waits for each port before starting the next one. |
| Stop All | Closes GameServer, ConnectServer, JoinServer, DataServer, then MariaDB (reverse order). The servers' "are you sure?" dialogs are confirmed automatically. |
| Rebuild Client | Writes the IP, runs the encoder and copies `main.exe`, `Main.dll`, `Data\Local\ClientInfo.bmd` into `Client\`. Use it after you edit `MainInfo.ini`. |
| Open Client | Starts `Client\main.exe`. |
| Check System | Checks .NET, Visual C++ x86 and your folders. |

The lights show whether each port is listening. The log is also saved to `manager.log`.

## What the manager changes
- `ConnectServer\ServerList.dat` and `MainInfo.ini`: **only the IP**. Everything else stays as in the original; the first original is kept as `*.orig`.
- `DataServer` and `JoinServer`: created from `MuServer\MySQL\...` if missing. Their `.ini` gets the local database settings (a random password for the `mu` database user, saved in `MuServer\DB\db-credentials.txt`; do not share that file).
- The database lives in `MuServer\DB\data`, MariaDB in `MuServer\DB\mariadb`. It listens on `127.0.0.1:3307` only.

## Ports
| Component | Port |
|---|---|
| MariaDB | 3307 (local only) |
| DataServer | 55980 |
| JoinServer | 55990 |
| ConnectServer | 44405 |
| GameServer | 55901 |

Players on other PCs only need TCP 44405 and 55901. Give them the `Client` folder built **after** you set your IP.

## Troubleshooting
- *Port is already used by another program*: another server instance is running. Stop it first.
- *Visual C++ Redistributable missing*: install the x86 version from the link above.
- *main.exe is running*: close the game before Rebuild Client.
- *main.exe / Main.dll in neither Client nor Encoder\Client*: copy them from the original Encoder package into `Encoder\Client`.
- Stuck at the server selection screen: the IP in `ServerList.dat` / `MainInfo.ini` does not match your PC. Fix the IP and press **Rebuild Client**.
- Send `manager.log` when asking for help.

## Customizing
- Language: the selector in the top right (English / Tieng Viet).
- Look: put `logo.png` (square; transparent or on a solid black background), `header.png` (wide banner, about 10:1, art on the right) and `icon.ico` in `Assets\`.

## License and credits
MIT License, see `LICENSE`. Third-party components: see `THIRD-PARTY-NOTICES.md`.
Server and client source: MuEmu 0.97k by Kayito (https://github.com/nicomuratona/MuEmu-0.97k-kayito).
This is an unofficial tool. All trademarks belong to their owners; it is not affiliated with any game publisher.
