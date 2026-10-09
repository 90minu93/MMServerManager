# Third-party notices

MMServer Manager itself is MIT licensed (see `LICENSE`). The release package also contains
or uses the following components, which keep their own licenses. This file is a notice,
not legal advice.

## MariaDB Server
- Not part of this release: the user downloads the official Windows ZIP from https://mariadb.org, and the manager extracts it unmodified on first run.
- License: GNU General Public License v2 (the license text is inside the ZIP, file `COPYING`).
- Source code and downloads: https://mariadb.org and https://github.com/MariaDB/server
- The manager only starts it as a separate program (127.0.0.1 only); it is not linked into the manager.

## MuEmu 0.97k (Kayito)
- The server, Encoder, SQL scripts and client folder are **not** part of this release. You download them
  yourself from GitHub (repository `MuEmu-0.97k-kayito` by `nicomuratona`) and they remain the work of their
  author, subject to whatever terms that repository states. This project is not affiliated with the author.
- On your own copy, the manager edits only the IP in `ConnectServer\ServerList.dat` and `MainInfo.ini`,
  copies the MySQL variant of DataServer / JoinServer into place, and keeps the first original of every
  file it touches as `*.orig`.
- The MySQL variant ships its own libraries (`mysqlcppconn-9-vs14.dll`, `libssl-1_1.dll`,
  `libcrypto-1_1.dll`), each under its own license.

## Not included
- Any client files. All trademarks belong to their owners; this tool is unofficial and not
  affiliated with any game publisher.
- Microsoft Visual C++ Redistributable 2015-2022 (x86). Install it from Microsoft if "Check System" asks for it.
