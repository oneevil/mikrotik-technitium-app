# MikroTik Address List — Technitium DNS App

[🇬🇧 English version](README.md)

Плагин для [Technitium DNS Server](https://technitium.com/dns/), который автоматически экспортирует IP-адреса из DNS-ответов в MikroTik firewall address-list через REST API.

## Как работает

1. Вы указываете список доменов (например `ya.ru`, `google.com`)
2. Когда любой клиент резолвит эти домены (включая поддомены), плагин перехватывает ответ
3. IP-адреса из A/AAAA записей отправляются в MikroTik address-list через REST API
4. По умолчанию DNS-ответ уходит сразу (фоновый режим). С `waitForMikrotik: true` ответ задерживается до подтверждения от MikroTik

### Особенности

- **Поддомены** — `sub.example.com` автоматически матчится если указан `example.com`
- **Дедупликация** — один IP не отправляется чаще раза в 30 секунд
- **TTL → timeout** — время жизни записи в address-list = TTL из DNS
- **IPv6** — AAAA записи отправляются в `/ipv6/firewall/address-list`
- **Non-blocking** — DNS-ответ не задерживается

## Установка

### Сборка

Требуется .NET SDK (9.0+):

```bash
cd mikrotik-technitium-app
dotnet build -c Release
cd bin/Release/net9.0
zip MikroTikAddressListApp.zip MikroTikAddressListApp.dll MikroTikAddressListApp.deps.json dnsApp.config
```

### Установка в Technitium

1. Откройте Technitium DNS Web Console → **Apps**
2. Нажмите **Install**
3. Имя: `MikroTik Address List`
4. Выберите `MikroTikAddressListApp.zip`
5. Нажмите **Config** → вставьте конфигурацию → **Save**

## Конфигурация

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

| Параметр | Описание |
|---|---|
| `enabled` | Включить/выключить плагин |
| `mikrotikUrl` | URL MikroTik REST API |
| `mikrotikUsername` | Логин |
| `mikrotikPassword` | Пароль |
| `addressListName` | Имя address-list в MikroTik |
| `useTtlAsTimeout` | Использовать DNS TTL как timeout записи |
| `defaultTimeout` | Timeout по умолчанию (если TTL выключен) |
| `enableIPv6` | Отправлять AAAA записи в `/ipv6/firewall/address-list` |
| `waitForMikrotik` | Ждать подтверждения от MikroTik перед отправкой DNS-ответа клиенту |
| `skipCertificateCheck` | Игнорировать ошибки SSL-сертификата |
| `domains` | Список отслеживаемых доменов |

## Проверка

```bash
# Сделать DNS-запрос через Technitium
dig ya.ru @<IP_DNS_СЕРВЕРА>

# Проверить address-list в MikroTik
/ip firewall address-list print where list=dns-resolved
```

## Требования

- Technitium DNS Server v14.3+
- MikroTik RouterOS v7+ (REST API)
- .NET SDK 9.0+ для сборки

## Лицензия

GPL-3.0
