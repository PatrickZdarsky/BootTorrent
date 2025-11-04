# BootTorrent

A distributed computer fleet management system designed for large-scale computer deployments. BootTorrent efficiently distributes bootable VHDX images to multiple machines using BitTorrent protocol and manages the boot process through MQTT-based communication.

## Overview

BootTorrent consists of three main components:

- **btserver**: Central management server that orchestrates deployments and communicates with clients
- **btclient**: Client agent running on managed machines that receives commands and reports status
- **boottorrent-lib**: Shared library containing common communication and transport logic

## Features

- 🚀 **Distributed Image Distribution**: Uses BitTorrent for efficient peer-to-peer transfer of boot images
- 📡 **MQTT Communication**: Real-time command and control using MQTT messaging
- 🏢 **Zone-Based Management**: Organize machines into zones for targeted deployments
- 📊 **Status Monitoring**: Real-time machine status reporting and heartbeat monitoring
- 🔒 **Secure Communication**: TLS support for MQTT connections
- 🐳 **Container Ready**: Docker support for easy deployment
- ☸️ **Kubernetes Support**: Helm charts included for Kubernetes deployments

## Architecture

BootTorrent uses an MQTT-based architecture for communication:

### Topic Structure

**Commands (Server → Clients):**
- `boottorrent/cmd/global/{messageType}` - Broadcast to all machines
- `boottorrent/cmd/zone/{zoneId}/{messageType}` - Commands to a specific zone
- `boottorrent/cmd/machine/{machineId}/{messageType}` - Commands to a specific machine

**Events (Clients → Server):**
- `boottorrent/evt/machine/{machineId}/{messageType}` - Machine status, errors, and events

All messages are encoded using MessagePack or JSON for efficiency.

For more details, see [MQTT Topic Plan](wiki/mqtt.md).

## Prerequisites

- **.NET 9.0 SDK** or later
- **MQTT Broker** (e.g., Mosquitto)
- **Docker** (optional, for containerized deployment)
- **Kubernetes** (optional, for Helm deployment)

## Installation

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/PatrickZdarsky/BootTorrent.git
   cd BootTorrent
   ```

2. Build the solution:
   ```bash
   dotnet build
   ```

3. Run tests (if available):
   ```bash
   dotnet test
   ```

### Docker Build

Build the server container:
```bash
docker build -f btserver/Dockerfile -t boottorrent-server .
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

## Running

### Running the Server

```bash
cd btserver
dotnet run
```

### Running the Client

```bash
cd btclient
dotnet run
```

### Using Docker

Run the server:
```bash
docker run -d \
  -v /path/to/appsettings.json:/app/appsettings.json \
  -p 1883:1883 \
  boottorrent-server
```

### Kubernetes Deployment

Deploy using Helm:

```bash
cd helm
helm install boottorrent . \
  --set mqtt.broker=your-mqtt-broker \
  --namespace boottorrent \
  --create-namespace
```

## Development

### Project Structure

```
BootTorrent/
├── boottorrent-lib/      # Shared library
│   ├── client/           # Machine and zone models
│   ├── communication/    # MQTT messaging layer
│   └── transport/        # Artifact deployment logic
├── btserver/             # Server application
│   └── handler/          # Message handlers
├── btclient/             # Client application
├── helm/                 # Kubernetes Helm charts
└── wiki/                 # Additional documentation
```

### Building for Release

```bash
dotnet publish -c Release
```

### Native AOT Compilation

Both server and client support Native AOT compilation for improved performance:

```bash
dotnet publish -c Release -r linux-x64
```

## Logging

Logs are written to:
- Console output (stdout)
- `logs/log.txt` (rotating daily)

Configure log levels in `appsettings.json`.

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Known Issues

- MessagePack package has a known moderate severity vulnerability (GHSA-4qm4-8hg2-g2xm)
- Some AOT compilation warnings related to configuration binding

## License

See the repository for license information.

## Support

For issues and questions, please use the GitHub issue tracker.
