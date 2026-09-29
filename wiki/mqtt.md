# BootTorrent MQTT topics

## Encoding
The current server and client entry points register `JsonMessageCodec`; current commands and events use JSON. A `MessagePackCodec` class exists in the library but is not selected by those entry points.

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

Current machine events include `startup`, `shutdown` and `heartbeat`. A run-tagged SHA-256 verified result and reset acknowledgement are planned for the evaluation; they are not present in the current message contract. `messageType` is the actual message type key, not the illustrative labels from an earlier topic proposal.

---

The existing machine-scoped command topic can target only the selected clients in an RQ1 run. The zone-scoped assignment command is paired with persisted server-side zone assignment state; later reconciliation for an offline client must use that state after the client reconnects and subscribes.
