# NetAudit Pro v4

Professional enterprise-grade Windows network diagnostic application built with .NET 6 WPF and modular architecture.

## Features

- **Network Analysis:** Packet capture, protocol analysis (ICMP, DNS, TCP, UDP, HTTP, SMB, LDAP, Kerberos)
- **Switch Diagnostics:** SSH-based remote configuration via Cisco, Aruba, HPE, Juniper, Fortinet profiles
- **Windows Infrastructure:** Active Directory, DNS, DHCP, Event Logs, PowerShell Remoting diagnostics
- **Professional Reporting:** PDF/HTML/Excel reports with QuestPDF
- **VMware Integration:** Virtual infrastructure monitoring
- **Linux Remote Diagnostics:** SSH-based remote system analysis
- **AI-Powered Analysis:** Anomaly detection and event correlation
- **Plugin System:** Extensible architecture for third-party vendors
- **Enterprise Logging:** Serilog structured logging to Documents folder
- **SQLite Database:** Local data persistence

## Architecture

9-module modular design:
- **NetAudit.Core** — Shared interfaces and data models
- **NetAudit.Database** — EF Core + SQLite context
- **NetAudit.UI** — WPF main application
- **NetAudit.PacketCapture** — SharpPcap integration
- **NetAudit.Network** — Network analysis engine
- **NetAudit.Windows** — Windows diagnostics
- **NetAudit.Switch** — SSH-based switch diagnostics
- **NetAudit.Reporting** — QuestPDF report generation
- **NetAudit.Plugins** — Plugin framework

## Building

### Windows (Recommended)

```powershell
# Clone and build
git clone https://github.com/yourusername/NetAudit.git
cd NetAudit
dotnet publish NetAudit.UI/NetAudit.UI.csproj -c Release -o ./dist
```

### Automated Build (GitHub Actions)

This repository includes Windows build automation via GitHub Actions. Push to main/develop branches triggers automatic Windows x64 build.

**Artifacts:**
- `NetAudit.UI.exe` (150 MB standalone executable)
- `NetAuditPro-*.exe` (InnoSetup installer)

## Installation

### Standalone Executable
```bash
.\dist\NetAudit.UI.exe
```

### InnoSetup Installer
```bash
.\Output\NetAuditPro-4.0.0-Setup.exe
```

Installer creates:
- Start Menu shortcuts
- Desktop icon
- Startup folder registration (optional)
- Logs directory: `Documents\NetAudit\logs\`
- Database directory: `Documents\NetAudit\data\`

## Runtime Requirements

- **Windows 7+** or **Windows Server 2016+**
- **No external dependencies** — executable is self-contained with embedded .NET 6 runtime
- Admin privileges (recommended for network capture and diagnostics)

## Deployment

1. Build on Windows with `dotnet publish`
2. Package with InnoSetup (netaudit-installer.iss)
3. Distribute Setup.exe to end users
4. Users click installer → one-click deployment
5. No console window, no .NET SDK required on client machines

## Development

### Requirements
- .NET 6 SDK
- Visual Studio 2022 or VS Code
- NuGet packages (auto-restore)

### Building from Source
```bash
dotnet build -c Release
dotnet test  # when tests added
```

### Running Locally
```bash
dotnet run --project NetAudit.UI
```

## Logging

Structured logging via Serilog to:
```
Documents\NetAudit\logs\netaudit-YYYY-MM-DD.log
```

## Database

SQLite local database at:
```
Documents\NetAudit\data\netaudit.db
```

Automatically created on first run with EF Core migrations.

## License

Internal use only. Contact TMMArchitecture for licensing.

---

**Version:** 4.0.0  
**Built:** 2026-07-17  
**Platform:** Windows x64
