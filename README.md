# ERGOPROXY

> **Cross-Platform, Terminal-Based, Device-Wide HTTP Proxy Tunnel Client**  
> Built with C# on .NET 10 LTS and Spectre.Console.

[![.NET 10 LTS](https://img.shields.io/badge/.NET-10%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Linux%20(TUN%20%2B%20Policy%20Routing)-informational)]()

---

## 1. Project Overview

**ERGOPROXY** creates a local, device-wide network tunnel that uses an existing **HTTP proxy server as the upstream network gateway for application traffic**.

On restricted networks (such as university or corporate Wi-Fi that block specific websites or apps), applications often lack native HTTP proxy configuration or ignore desktop proxy environment variables. ERGOPROXY solves this by:

1. Establishing a local virtual network interface (`ergo0` TUN device).
2. Intercepting device-wide application network traffic transparently.
3. Transporting TCP connections through the upstream HTTP proxy using standard **HTTP `CONNECT` tunnels**.
4. Intercepting DNS lookups with a built-in **Fake-IP resolver**, delegating destination host resolution directly to the upstream proxy to defeat campus DNS censorship and domain blocklists.
5. Preserving end-to-end TLS encryption with **zero TLS decryption or custom root CA certificates**.
6. Preventing routing loops via Linux policy routing (`fwmark 0x4552`).
7. Providing a terminal UI (TUI) and non-interactive command-line interface.

---

## 2. How It Works

```
                     Applications (Browsers, Discord, CLI, Games)
                                         │
                                         ▼
                     ┌───────────────────────────────────────┐
                     │         Kernel Routing Layer          │
                     │  ip rule: not fwmark 0x4552 → table   │
                     └───────────────────┬───────────────────┘
                                         │
                                         ▼
                     ┌───────────────────────────────────────┐
                     │          TUN Interface: ergo0         │
                     │             198.18.0.1/15             │
                     └───────────────────┬───────────────────┘
                                         │
                                         ▼
                     ┌───────────────────────────────────────┐
                     │          ErgoProxy Engine             │
                     │  - TCP: NAT rewrite → Relay listener  │
                     │  - UDP 53: Fake-IP DNS resolver       │
                     │  - Other UDP: ICMP port unreachable   │
                     │    (forces browsers to TCP fallback)  │
                     └───────────────────┬───────────────────┘
                                         │
                                         ▼
                     ┌───────────────────────────────────────┐
                     │             TCP Relay                 │
                     │  HTTP CONNECT destination:port        │
                     │  Proxy-Authorization: Basic ...       │
                     │  Marked with SO_MARK 0x4552           │
                     └───────────────────┬───────────────────┘
                                         │
                                         ▼
                              Upstream HTTP Proxy
                             (e.g., Campus Squid)
                                         │
                                         ▼
                                     Internet
```

---

## 3. Protocol Transparency & Honesty

| Protocol | Status | Behavior & Rationale |
|---|---|---|
| **TCP / IPv4 (HTTPS, WebSockets, Sockets)** | ✅ Supported | Tunnelled transparently via HTTP `CONNECT`. TLS remains end-to-end encrypted. |
| **HTTP (port 80)** | ✅ Supported | Forwarded through CONNECT or direct proxy requests. |
| **DNS (Domain Resolution)** | ✅ Supported | Virtual Fake-IP resolver (`198.19.0.0/16`). Destination names are resolved by the proxy, bypassing local DNS tampering. |
| **UDP (QUIC / HTTP/3, WebRTC, Games)** | ❌ Rejected | Standard HTTP proxies cannot transport raw UDP. Rejected with ICMP port unreachable so modern clients (browsers, Discord) quickly fall back to TCP. |
| **IPv6** | 🔒 Blocked | Captured and cleanly rejected via ICMPv6 to prevent leaks; applications fall back to IPv4. |
| **ICMP (ping)** | ❌ Unsupported | HTTP proxies cannot carry ICMP echo packets. |

---

## 4. Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- Linux kernel with `CONFIG_TUN=y` and `iproute2`
- Root privileges (`sudo`) are required only for establishing the TUN device and policy routing.

### Build from Source

```bash
git clone https://github.com/ergoproxy/ergoproxy.git
cd ergoproxy
dotnet build ErgoProxy.slnx
```

### Configure a Proxy Profile

```bash
# Add a campus proxy
dotnet run --project src/ErgoProxy.Cli -- configure --name "Campus" --host "10.10.127.3" --port 3128

# Add an authenticated proxy
dotnet run --project src/ErgoProxy.Cli -- configure --name "AuthProxy" --host "proxy.college.edu" --port 8080 --auth --username "student" --password "secret"
```

### Test Proxy Connectivity

```bash
dotnet run --project src/ErgoProxy.Cli -- test
```

### Start the Device-Wide Tunnel

```bash
dotnet run --project src/ErgoProxy.Cli -- connect
```

You can now use any app (browser, curl, Discord, Git, package managers) without configuring individual proxy settings:

```bash
# Verify external connectivity through the tunnel
curl -I https://www.google.com
```

### Stop the Tunnel

```bash
dotnet run --project src/ErgoProxy.Cli -- disconnect
```

Stopping the tunnel cleanly removes all policy routing rules, tears down `ergo0`, and restores default system networking.

---

## 5. Command-Line Reference

| Command | Description |
|---|---|
| `ergoproxy` | Launch interactive Terminal UI (TUI) with live dashboard |
| `ergoproxy configure` | Create or update a named proxy profile |
| `ergoproxy profiles` | List all saved proxy profiles (`--json` supported) |
| `ergoproxy test` | Test proxy DNS, TCP, 407 authentication, and HTTPS CONNECT |
| `ergoproxy connect [name]` | Start device-wide tunnel through the chosen profile |
| `ergoproxy disconnect` | Stop tunnel and restore previous system networking |
| `ergoproxy status` | Display tunnel state, active flows, and RX/TX bandwidth |
| `ergoproxy recover` | Emergency cleanup of stale TUN interfaces or routing rules |
| `ergoproxy daemon` | Run the tunnel controller as a background service |

---

## 6. Security Model

1. **Zero Secret Leakage:** Passwords are stored in the OS keyring (Secret Service on Linux) or an encrypted AES-256-GCM vault. Passwords never appear in plaintext logs, configuration files, or status dashboards.
2. **Reversibility Guarantee:** All routing rules, table entries, and DNS changes are tracked in `/run/ergoproxy/applied_state.json` and restored on disconnect or crash.
3. **No TLS Interception:** Application TLS sessions are transported untouched. The proxy cannot inspect application payloads.
4. **Loop Prevention:** All sockets created by ErgoProxy are marked with `SO_MARK 0x4552`, ensuring proxy traffic bypasses the tunnel even if the proxy's IP changes dynamically.

---

## 7. License

Licensed under the MIT License. See [LICENSE](LICENSE) for details.
