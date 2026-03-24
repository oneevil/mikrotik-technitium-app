# MikroTik Address List — Technitium DNS App

[🇷🇺 Русская версия](README.ru.md)

A plugin for [Technitium DNS Server](https://technitium.com/dns/) that automatically exports resolved IP addresses to a MikroTik firewall address-list via the RouterOS REST API.

## How It Works

1. You specify a list of domains (e.g. `ya.ru`, `google.com`)
2. When any client resolves these domains (including subdomains), the plugin intercepts the response
3. IP addresses from A/AAAA records are sent to the MikroTik address-list via REST API
4. By default, the DNS response is returned immediately (background mode). With `waitForMikrotik: true`, the response is held until MikroTik confirms

### Features

- **Subdomains** — `sub.example.com` automatically matches if `example.com` is configured
- **Deduplication** — the same IP is not sent more than once every 30 seconds
- **TTL → timeout** — address-list entry timeout equals the DNS TTL
- **IPv6** — AAAA records are sent to `/ipv6/firewall/address-list`
- **Non-blocking** — DNS response is never delayed

## Installation

### Build

Requires .NET SDK (9.0+):

```bash
cd mikrotik-technitium-app
dotnet build -c Release
cd bin/Release/net9.0
zip MikroTikAddressListApp.zip MikroTikAddressListApp.dll MikroTikAddressListApp.deps.json dnsApp.config
```

### Install in Technitium

1. Open Technitium DNS Web Console → **Apps**
2. Click **Install**
3. Name: `MikroTik Address List`
4. Select `MikroTikAddressListApp.zip`
5. Click **Config** → paste configuration → **Save**

## Configuration

```json
{
  "enabled": true,
  "mikrotikUrl": "https://192.168.88.1",
  "mikrotikUsername": "admin",
  "mikrotikPassword": "password",
  "addressListName": "dns-resolved",
  "useTtlAsTimeout": true,
  "defaultTimeout": "00:05:00",
  "enableIPv6": false,
  "waitForMikrotik": false,
  "skipCertificateCheck": true,
  "domains": [
    "ya.ru",
    "google.com"
  ]
}
```

| Parameter | Description |
|---|---|
| `enabled` | Enable/disable the plugin |
| `mikrotikUrl` | MikroTik REST API URL |
| `mikrotikUsername` | Login |
| `mikrotikPassword` | Password |
| `addressListName` | MikroTik address-list name |
| `useTtlAsTimeout` | Use DNS TTL as address-list entry timeout |
| `defaultTimeout` | Default timeout (when TTL mode is off) |
| `enableIPv6` | Send AAAA records to `/ipv6/firewall/address-list` |
| `waitForMikrotik` | Wait for MikroTik confirmation before returning DNS response |
| `skipCertificateCheck` | Ignore SSL certificate errors |
| `domains` | List of monitored domains |

## Verification

```bash
# Make a DNS query through Technitium
dig ya.ru @<DNS_SERVER_IP>

# Check address-list in MikroTik
/ip firewall address-list print where list=dns-resolved
```

## Requirements

- Technitium DNS Server v14.3+
- MikroTik RouterOS v7+ (REST API)
- .NET SDK 9.0+ (to build)

## License

GPL-3.0
