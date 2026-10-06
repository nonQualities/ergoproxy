# ERGOPROXY

> **Cross-Platform HTTP Proxy Manager**  
> Built with C# on .NET 10 LTS and Spectre.Console for Linux, Windows, and macOS.

[![.NET 10 LTS](https://img.shields.io/badge/.NET-10%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20Windows%20%7C%20macOS-informational)]()

---

## Table of Contents

1. [Introduction](#introduction)
2. [Key Features](#key-features)
3. [Architecture](#architecture)
4. [Prerequisites & Installation](#prerequisites--installation)
5. [Interactive Terminal UI (TUI)](#interactive-terminal-ui-tui)
6. [Command-Line Interface (CLI)](#command-line-interface-cli)
7. [Platform Adapters](#platform-adapters)
8. [Credential Security](#credential-security)
9. [Recovery & Troubleshooting](#recovery--troubleshooting)
10. [Limitations](#limitations)
11. [Testing & Acceptance](#testing--acceptance)
12. [License](#license)

---

## 1. Introduction

**ERGOPROXY** is a cross-platform desktop terminal application for configuring and managing HTTP proxy connections on desktop operating systems. It enables users to define proxy profiles, optionally configure authentication credentials, test proxy connectivity, and enable or disable system proxy settings.

The application acts as a **proxy configuration client**, not as a proxy server or a VPN implementation. It simplifies configuring an existing HTTP/HTTPS proxy without requiring manual operating-system registry editing, network service configuration, or environment variable management.

---

## 2. Key Features

- **Interactive Terminal UI:** Rich, keyboard-driven interface powered by `Spectre.Console` with spinners, status badges, and dashboards.
- **Persistent Proxy Profiles:** Named, versioned JSON configuration with automatic validation, duplicate checking, and atomic writes.
- **Secure Credential Management:** Secrets stored in OS keyrings (Secret Service, Windows Credential Manager, macOS Keychain) or an encrypted AES-256-GCM vault. Zero plain-text passwords in logs or profile files.
- **Deep Proxy Connectivity Diagnostics:** Multi-stage diagnostic pipeline testing DNS resolution, TCP sockets, HTTP requests, authentication challenges (407), HTTPS `CONNECT` tunnels, and TLS handshakes.
- **Safe System Proxy Integration:** Applies system proxy settings and records exact snapshots of previous OS settings for faithful restoration on deactivation.
- **Conflict & Recovery Detection:** Detects when external applications tamper with system settings and provides clean recovery procedures.
- **Non-Interactive CLI:** Full command set (`configure`, `profiles`, `test`, `connect`, `disconnect`, `status`, `help`) with standard exit codes for automated scripts.

---

## 3. Architecture

ERGOPROXY follows a clean, modular architecture:

```
+-----------------------------------------------------------------------------------+
|                                  ERGOPROXY CLI & TUI                              |
|   +---------------------------------------+   +-------------------------------+   |
|   |         Spectre.Console TUI           |   |       CLI Subcommands         |   |
|   |  - Interactive Menus & Prompts        |   |  - configure, test, connect   |   |
|   |  - Dashboard, Status, Help            |   |  - disconnect, status, profiles|  |
|   +---------------------------------------+   +-------------------------------+   |
+-----------------------------------------+-----------------------------------------+
                                          |
+-----------------------------------------v-----------------------------------------+
|                                ErgoProxy.Core Layer                               |
|  +--------------------+  +----------------------+  +----------------------------+ |
|  | Models & Validation|  | Storage & Persistence|  | Security & Redaction       | |
|  | - ProxyProfile     |  | - IProfileRepository |  | - SecretRedactor           | |
|  | - ProfileValidator |  | - IStateManager      |  | - ICredentialStore         | |
|  | - RuntimeState     |  | - Versioned JSON     |  |   (OS Keyring + AES Vault) | |
|  +--------------------+  +----------------------+  +----------------------------+ |
|                                                                                   |
|  +------------------------------------------------------------------------------+ |
|  | Proxy Diagnostic & Testing Engine (IProxyTester)                             | |
|  | - DNS Resolution -> TCP Connection -> Direct HTTP (Auth) -> CONNECT Tunnel   | |
|  | - TLS Handshake -> HTTP over TLS -> Error Classification                     | |
|  +------------------------------------------------------------------------------+ |
|                                                                                   |
|  +------------------------------------------------------------------------------+ |
|  | Platform Integration Layer (IPlatformAdapter)                                 | |
|  | +-------------------+  +---------------------+  +--------------------------+ | |
|  | | LinuxAdapter      |  | WindowsAdapter      |  | MacOSAdapter             | | |
|  | | (GNOME/KDE/Env)   |  | (WinINet/WinHTTP)   |  | (networksetup)           | | |
|  | +-------------------+  +---------------------+  +--------------------------+ | |
|  | - Snapshot previous settings -> Apply -> Verify -> Safe Restoration / Rollback| |
|  +------------------------------------------------------------------------------+ |
+-----------------------------------------------------------------------------------+
```

- **`ErgoProxy.Core`**: Domain models, validators, storage, diagnostic engine, and platform adapters. Free of external third-party dependencies.
- **`ErgoProxy.Cli`**: Console application providing both the interactive Spectre.Console TUI and non-interactive command-line operations.
- **`ErgoProxy.Tests`**: Automated acceptance and unit tests covering all 15 acceptance criteria (`AT-01` to `AT-15`).

---

## 4. Prerequisites & Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- Supported OS:
  - **Linux**: GNOME (`gsettings`), KDE Plasma (`kioslaverc`), or headless environments (with manual shell exports).
  - **Windows**: Windows 10/11 desktop.
  - **macOS**: macOS 12+ (`networksetup`).

### Build from Source

```bash
git clone https://github.com/ergoproxy/ergoproxy.git
cd ergoproxy
dotnet build ErgoProxy.sln
```

---

## 5. Interactive Terminal UI (TUI)

Launch the interactive interface by running `ergoproxy` with no arguments:

```bash
dotnet run --project src/ErgoProxy.Cli
```

The interactive interface provides:
1. **Connect (Enable System Proxy):** Applies the active profile to the OS with real-time verification and conflict detection.
2. **Disconnect (Restore System Proxy):** Deactivates the managed proxy and faithfully restores previous OS settings.
3. **Profile Management:** Interactive wizards to create, edit, select, or delete proxy profiles.
4. **Test Proxy Connectivity:** Live visual spinner while running the multi-stage diagnostic test suite.
5. **View Detailed Status:** Visual dashboard displaying active endpoint, system proxy state, and last test report.
6. **Manage Credentials:** Secure masked password prompts for adding or rotating proxy credentials.
7. **System Diagnostics & Recovery:** Troubleshoot conflicts or reset system state after interrupted sessions.
8. **Help & Documentation:** Built-in guidance and reference tables.

---

## 6. Command-Line Interface (CLI)

ERGOPROXY supports full non-interactive automation for scripting and CI/CD pipelines.

### Command Reference

#### `configure`
Create or update a named proxy profile:
```bash
ergoproxy configure --name "Campus" --host "proxy.college.edu" --port 8080 --bypass "localhost,127.0.0.1"
```
With authentication:
```bash
ergoproxy configure --name "SecureProxy" --host "proxy.corp.com" --port 3128 --auth --username "student" --password "secret"
```

#### `profiles`
List all configured profiles:
```bash
ergoproxy profiles
ergoproxy profiles --json
```

#### `test`
Run connectivity diagnostics against a profile:
```bash
ergoproxy test
ergoproxy test --id <profile-id> --timeout 10
ergoproxy test --json
```

#### `connect`
Activate a profile as the active system proxy:
```bash
ergoproxy connect
ergoproxy connect --id <profile-id> --force
```

#### `disconnect`
Disable system proxy and restore previous settings:
```bash
ergoproxy disconnect
ergoproxy disconnect --force
```

#### `status`
Inspect current configuration and runtime state:
```bash
ergoproxy status
ergoproxy status --json
```

#### `help`
Display command syntax and exit code documentation:
```bash
ergoproxy help
```

### Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Configuration or validation error |
| `2` | Network or proxy connectivity failure |
| `3` | Platform conflict or unsupported operation |

---

## 7. Platform Adapters

### Linux
- **GNOME Desktop:** Manages `org.gnome.system.proxy` via `gsettings` (`mode 'manual'`, `http host/port`, `https host/port`, `ignore-hosts`). Restores previous mode on disconnect.
- **KDE Plasma:** Configures `kioslaverc` [Proxy Settings] via `kwriteconfig5` or `kwriteconfig6`.
- **Headless / Non-desktop:** Detects unsupported environments, explains limitations, and guides users on exporting environment variables (`http_proxy`, `https_proxy`, `all_proxy`).

### Windows
- **WinINet:** Manages `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings` (`ProxyEnable`, `ProxyServer`, `ProxyOverride`).
- **WinHTTP Distinction:** WinINet controls browser and desktop desktop applications. Services relying on WinHTTP (`netsh winhttp`) are documented separately.

### macOS
- **Network Services:** Queries `/usr/sbin/networksetup -listnetworkserviceorder` to discover the primary active network service (e.g. `Wi-Fi` or `Ethernet`).
- **Proxy Management:** Configures web proxy (`-setwebproxy`), secure web proxy (`-setsecurewebproxy`), bypass domains (`-setproxybypassdomains`), and toggles states.

---

## 8. Credential Security

1. **Separation of Secrets:** Profile configuration (`profiles.json`) only references credentials via unique tokens; passwords are never serialized to profile JSON.
2. **Encrypted Storage:** Secrets are stored in OS-native facilities (`secret-tool` on Linux, DPAPI on Windows, Keychain on macOS) with an AES-256-GCM encrypted file fallback protected by user and machine unique keys.
3. **Redaction:** `SecretRedactor` automatically masks passwords and Basic authentication tokens in URLs, headers, and logs (`***`).

---

## 9. Recovery & Troubleshooting

### Interrupted Sessions
If ERGOPROXY is interrupted while a proxy is active:
1. Re-launch `ergoproxy`.
2. Inspect the dashboard: the pending recovery alert will appear.
3. Select **System Diagnostics & Recovery** or run `ergoproxy disconnect --force` to restore your system settings.

### Manual OS Reset Instructions
- **Linux (GNOME):** `gsettings set org.gnome.system.proxy mode 'none'`
- **Linux (KDE):** Set `ProxyType=0` in `~/.config/kioslaverc`
- **Windows:** In Settings, go to **Network & Internet -> Proxy** and turn off **Use a proxy server**.
- **macOS:** Run `networksetup -setwebproxystate "Wi-Fi" off` and `networksetup -setsecurewebproxystate "Wi-Fi" off`.

---

## 10. Limitations

- **Not a VPN:** ERGOPROXY does not provide packet-level tunneling, TUN/TAP interfaces, UDP forwarding, or DNS leak protection.
- **Application Compatibility:** Desktop applications that bypass system proxy configurations (or use their own hardcoded configurations) will not automatically route through the proxy.
- **No TLS Decryption:** The application does not install root certificates or intercept HTTPS traffic.

---

## 11. Testing & Acceptance

The test suite validates the 15 acceptance criteria:

| ID | Test Case | Status |
|---|---|---|
| **AT-01** | Create valid profile | Verified |
| **AT-02** | Invalid port validation | Verified |
| **AT-03** | Missing host rejection | Verified |
| **AT-04** | Proxy without authentication | Verified |
| **AT-05** | Authenticated proxy (valid & invalid 407) | Verified |
| **AT-06** | Unreachable endpoint timeout | Verified |
| **AT-07** | HTTPS tunnelling through HTTP CONNECT | Verified |
| **AT-08** | Enable system proxy configuration | Verified |
| **AT-09** | Disable system proxy and restore settings | Verified |
| **AT-10** | External settings conflict detection | Verified |
| **AT-11** | Secret redaction in logs and storage | Verified |
| **AT-12** | Unsupported environment handling | Verified |
| **AT-13** | Profile and state persistence across restarts | Verified |
| **AT-14** | Interrupted activation and rollback | Verified |
| **AT-15** | Non-interactive CLI command exit codes | Verified |

Run tests:
```bash
dotnet test tests/ErgoProxy.Tests/ErgoProxy.Tests.csproj
```

---

## 12. License

This project is licensed under the [MIT License](LICENSE).
