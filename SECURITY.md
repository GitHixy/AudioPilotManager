# Security

## What Audio Pilot Manager can and can't do

- Its only network connection is the **update check** to the GitHub releases API (once a day, can be turned off in Settings → Updates). An update is downloaded only when you choose to install it, only from this repository's release files, and it is checked against the published `SHA256SUMS.txt` before it runs. The other external links (Patreon and GitHub) open in your browser, and only when you click them.
- It runs **without administrator rights** and requests none (`asInvoker` manifest).
- It uses the documented Windows Core Audio APIs to read and change volumes and to list audio sessions, plus the Sound control panel's interface to change the default device. It never installs drivers, modifies system files or injects code into other processes.
- It writes only to:
  - `%AppData%\AudioPilotManager\settings.json` (your settings and profiles),
  - `%LocalAppData%\AudioPilotManager\logs\` (a small rotating log),
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `AudioPilotManager`, only if you turn on *Start with Windows*.
- The optional live microphone meter opens a shared capture stream while a window is visible and releases every buffer unread. No audio is recorded, stored or transmitted.

## Verifying a download

Every release includes `SHA256SUMS.txt`. Compare it with:

```powershell
Get-FileHash .\AudioPilotManager-<version>-Setup.exe -Algorithm SHA256
```

Releases are built by GitHub Actions from the tagged source (see `.github/workflows/release.yml`), and you can reproduce them with `./build.ps1`.

## Reporting a vulnerability

Please report security issues privately through GitHub's **Report a vulnerability** button on the repository's *Security* tab, rather than in a public issue.
