# BootTorrent

BootTorrent is a distributed artifact distribution and fleet coordination system designed for large-scale computer environments. It combines centrally managed orchestration with controlled peer-to-peer distribution using BitTorrent technology to efficiently distribute large artifacts across network zones.

The project focuses on reducing central infrastructure load while maintaining centralized control over distribution policies, topology awareness, and client coordination.

## Overview

BootTorrent currently consists of three main components:

- **btserver** – Central coordination server responsible for orchestration, assignments, topology management, and monitoring
- **btclient** – Client agent running on managed machines that receives assignments, participates in torrent distribution, and reports status
- **boottorrent-lib** – Shared library containing communication contracts, messaging infrastructure, and transport logic

## Features

- Distributed artifact distribution using BitTorrent
- Custom tracker implementation
- Embedded MonoTorrent client integration
- MQTT-based command and control communication
- Zone-aware distribution topology
- Dynamic artifact assignments
- Real-time client monitoring and heartbeat tracking
- Strongly typed messaging contracts
- PostgreSQL-backed configuration management
- Docker support
- Kubernetes deployment support via Helm

## Architecture

BootTorrent follows a modular distributed architecture consisting of:

- A central coordination layer
- A messaging and event system
- A torrent-based transport layer
- Distributed clients participating in artifact propagation

### Communication Model

BootTorrent uses MQTT for orchestration and status communication.

### Topic Structure

**Commands (Server → Clients)**

- `boottorrent/cmd/global/{messageType}` – Broadcast commands
- `boottorrent/cmd/zone/{zoneId}/{messageType}` – Zone-targeted commands
- `boottorrent/cmd/machine/{machineId}/{messageType}` – Machine-specific commands

**Events (Clients → Server)**

- `boottorrent/evt/machine/{machineId}/{messageType}` – Status updates, heartbeats, and events

Messages are serialized using MessagePack or JSON.

For more details, see [MQTT Topic Plan](wiki/mqtt.md).

## Current State

Implemented functionality includes:

- Fully custom BitTorrent tracker implementation
- Functional torrent distribution using MonoTorrent clients and seeders
- Artifact assignment handling on clients
- Initial PostgreSQL integration for persistent configuration and zone management
- Continuous client monitoring and status tracking
- Typed event-driven messaging infrastructure

Planned functionality includes:

- Policy enforcement for controlled proxy downloads
- Topology-aware intra-zone distribution optimization
- REST API for configuration and orchestration
- Extended management functionality for artifacts, clients, and assignments
- Performance evaluation and benchmarking

## Technology Stack

- .NET 10
- MQTT
- MonoTorrent
- PostgreSQL
- Docker
- Kubernetes + Helm

## Prerequisites

- .NET 10 SDK or later
- MQTT broker (e.g. Mosquitto)
- PostgreSQL
- Docker (optional)

## Installation

### Clone Repository

```bash
git clone https://github.com/PatrickZdarsky/BootTorrent.git
cd BootTorrent
```

### Build

```bash
dotnet build
```

### Run Tests

```bash
dotnet test
```

## Configuration

### Server Configuration

Edit `btserver/appsettings.json`:

```json
{
  "Mqtt": {
    "ClientId": "btserver",
    "Server": "mqtt-broker-address",
    "Port": 1883,
    "UseTLS": false,
    "Username": "",
    "Password": ""
  },
  "Postgres": {
    "ConnectionString": "Host=localhost;Database=boottorrent;"
  }
}
```

### Client Configuration

Edit `btclient/appsettings.json`:

```json
{
  "Client": {
    "ClientIdentifier": "unique-machine-id"
  },
  "Mqtt": {
    "ClientId": "btclient-{id}",
    "Server": "mqtt-broker-address",
    "Port": 1883,
    "UseTLS": false,
    "Username": "",
    "Password": ""
  }
}
```


## Project Structure

```text
BootTorrent/
├── boottorrent-lib/      # Shared library
│   ├── client/           # Client and zone models
│   ├── communication/    # MQTT messaging layer
├── btserver/             # Central coordination server
├── btclient/             # Client runtime
├── helm/                 # Kubernetes Helm charts
└── wiki/                 # Documentation
```

## Native AOT

Both server and client support Native AOT compilation.

```bash
dotnet publish -c Release -r linux-x64
```

## Logging

Logs are written to:

- Console output
- `logs/log.txt`

Log levels can be configured in `appsettings.json`.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push the branch
5. Open a Pull Request

## Known Issues

- Some Native AOT warnings related to configuration binding
- REST management API not yet implemented
- Proxy policy enforcement still missing

## License

See repository license information.

## Support

Please use the GitHub issue tracker for bug reports, discussions, and feature requests.