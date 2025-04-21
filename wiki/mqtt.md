# BootTorrent MQTT Topic Plan

## Encoding
All MQTT messages are encoded using [MessagePack](https://msgpack.org/) for efficiency.

---

## Topic Structure Overview

The topic hierarchy is split by message **intent**:

- `cmd/` – Commands and control messages (Server ➝ Clients)
- `evt/` – Events and feedback messages (Clients ➝ Server)


## Topics

### Server ➝ Clients

#### Global Command
`boottorrent/cmd/global/{messageType}` 
Used by the server to broadcast commands to all machines.
#### Zone Command
`boottorrent/cmd/zone/{zoneId}/{messageType}`
Used by the server to send commands to all machines within a specific zone.
#### Machine Command
`boottorrent/cmd/machine/{machineId}/{messageType}`
Used by the server to send commands to a specific machine.

### Clients ➝ Server

#### Machine Events
`boottorrent/evt/machine/{machineId}/{messageType}`
Used by a machine to report events or feedback to the server. `messageType` defines the kind of feedback.

##### Example Message Types:
- `status` – Periodic status or heartbeat
- `download` – Torrent download progress or completion
- `boot` – Boot state notifications
- `error` – Errors encountered by the client
- `info` – Optional logs or diagnostic info

---

## Benefits of this Structure
- Clear directionality (commands vs. feedback)
- Easy topic filtering (e.g., `boottorrent/evt/#`)
- Scalable for additional types of events or devices
- Clean separation of concerns,