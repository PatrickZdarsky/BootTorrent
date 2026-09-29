# BootTorrent

BootTorrent is a prototype for centrally managed artifact distribution with BitTorrent data transfer. Its motivating setting is a school with many computer labs: a typical room has about 36 PCs, and two larger rooms have up to about 80. Each PC has a 1 Gbit/s switch connection, while a lab commonly shares a 2 × 10 Gbit/s LACP uplink. The switch fabric can carry much more local traffic than that uplink. Distributing pieces among PCs in the room could use this local capacity and reduce repeated transfers from the central server.

The project distinguishes **control traffic** from **artifact payload**. The server assigns artifacts, tracks machines and zones, and decides which peers a client may discover. Clients transfer the artifact using BitTorrent. The goal is to measure completion time and where the payload bytes travel; lower server traffic does not imply lower total network traffic or faster completion.

## Intended zone distribution

A **zone** represents machines that should exchange artifact data locally. In the motivating deployment, a computer lab generally has its own VLAN on its room switch. A multi-switch large room uses high-throughput stacking links. The current zone-aware tracker policy operates on subnet zones; the intended physical meaning and the actual network path must be recorded for each deployment.

When an artifact is assigned to a zone, the server designates a configurable small number of its clients as **proxies**. A proxy is a normal `btclient` instance with permission to obtain artifact payload directly from the central seeder. It is neither a separate gateway program nor the only uploader in the zone. Proxies and ordinary clients should all exchange pieces with available peers in their zone, including ordinary-client-to-ordinary-client transfers.

**Ordinary clients must never receive artifact payload from the central seeder in zone-aware mode.** If a selected proxy is unavailable, clients should use other zone peers that already have the needed pieces. If there is no viable proxy and no client in the zone holds a complete copy, management must designate another available client as a proxy before more source data is fetched. If a complete copy is already available and seeding in the zone, a newly available client can obtain the entire artifact locally, with zero central-server **payload** traffic for that client's download. This depends on that peer remaining available and discoverable; tracker, assignment, and status traffic may still contact central services.

The main policy parameter is the number of proxies per zone. Selection could later use machine load or observed uptime; rotating proxies on a fixed short timer is not a design requirement. Transfers between zones are outside the primary zone-local design. They could be studied as a separate policy when reducing central-server load matters more than keeping payload within each zone.

See [the architecture note](wiki/architecture.md) for the intended assignment and recovery sequence.

## Components and communication

| Project | Responsibility |
| --- | --- |
| `btserver` | Artifact registration, zone assignments, machine state, REST management endpoints, custom tracker, and central MonoTorrent seeder |
| `btclient` | MQTT agent, assignment handling, local MonoTorrent transfer, and status reporting |
| `boottorrent-lib` | Shared artifact, torrent-job, and messaging types |

MQTT carries commands from server to clients on `boottorrent/cmd/global/{messageType}`, `boottorrent/cmd/zone/{zoneId}/{messageType}`, and `boottorrent/cmd/machine/{machineId}/{messageType}`. Clients report events on `boottorrent/evt/machine/{machineId}/{messageType}`. The current applications register JSON message codecs. Torrent metadata and tracker announces use HTTP; artifact pieces use BitTorrent peer connections. See [the MQTT topic plan](wiki/mqtt.md) for topic details.

## Current implementation status

The checkout contains .NET 10 server, client, and shared-library projects; a custom tracker; MonoTorrent integration; MQTT registration, assignments, and heartbeats; SQLite and Entity Framework migrations; and REST controllers for artifacts, machines, zones, and policy configuration. Docker and Helm files are present. These code paths have **not** been established here as a working end-to-end deployment or benchmark.

Several behaviors still differ from the intended zone design:

- The current subnet-zone tracker policy is coded to return central-seeder peers to designated proxies and only proxy peers to ordinary clients. It does not yet return all eligible ordinary peers within the zone. When it finds no announced proxy peer, it tries to return central-seeder peers to an ordinary client. That fallback violates the rule above and must be removed or replaced with proxy reassignment while preserving local peer access. Seeder registration is currently missing, so these responses may be empty in practice.
- Proxy IDs can be configured, and the policy fills vacant proxy slots from active zone machines. Selection currently follows machine-ID order; it does not use load or uptime. Availability of a complete zone copy is not yet used to decide whether a replacement proxy needs central access.
- Zone artifact assignments are published as MQTT commands. The current startup path does not reconcile every artifact assigned to a zone for a machine that was offline when the assignment was published. Late-join behavior therefore needs implementation and verification.
- Central-seeder registration, verified client completion, and observed payload paths need end-to-end checking before source-traffic or locality claims can be made. Tracker peer lists are discovery decisions, not measurements of transferred bytes.

The lower-priority `random-fallback` tracker policy is an unrestricted peer-selection policy for requests that the subnet-zone policy does not handle. It is not a recovery path that should grant ordinary zone clients central-seeder access.

## Build and configuration

The solution targets .NET 10. A local build requires the .NET 10 SDK and restored dependencies:

```bash
dotnet build BootTorrent.sln
```

Running the applications also requires an MQTT broker. The server uses the `ConnectionStrings:DefaultConnection` setting for SQLite. Server MQTT and torrent settings are under `Mqtt` and `Torrent`; the client uses `Mqtt` and `Client`. The development settings files contain machine-specific paths and loopback addresses, so replace them with paths, broker details, and a tracker URL reachable by the clients. Give every client a unique `Client:ClientIdentifier` and MQTT `Mqtt:ClientId`.

The client project enables Native AOT publishing. Docker and Helm assets are available in the repository, but their deployment behavior is not verified by this README.

## Thesis workspace

The parent workspace contains the current [project knowledge](../Project-Knowledge.md), [research questions](../Research-Questions.md), and [evaluation setup](../Evaluation-Setup.md). Those working documents distinguish intended behavior from code review findings and measured results.
