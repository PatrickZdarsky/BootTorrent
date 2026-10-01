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

Proxy election is now managed per distribution zone and artifact. The server stores a desired initial count and a selection policy for each distribution zone, keeps effective designations and transitions separately, and reconciles them on machine and assignment changes. Surviving proxies are retained without automatic refill. If the last proxy disappears, one replacement is selected only when no online assigned machine reports a complete copy. The tracker returns same-zone announced peers to clients and the central seeder only to designated proxies. Unknown or ambiguous clients receive no peers. The unrestricted random policy is not registered in normal operation.

The complete control and transfer sequence is documented in [Proxy election and artifact distribution flow](wiki/proxy-election-flow.md).

The server migrates its SQLite database at startup. The former subnet-zone proxy count and IDs are migrated to the new configuration as an initial count and preferred IDs. The default distribution zone has a stable ID and supports artifact assignment through the ordinary artifact-zone endpoint.

Proxy APIs:

- `GET/PUT /api/v1/policies/zones/{zoneId}/proxies`: desired count, selection policy key, and preferred IDs.
- `GET/PUT /api/v1/policies/zones/{zoneId}/artifacts/{artifactId}/proxies`: inspect or explicitly designate the online proxy machines for an artifact.
- `GET /api/v1/policies/zones/{zoneId}/artifacts/{artifactId}/proxy-transitions`: recent election changes.

End-to-end transfer and payload-path verification is still required. Tracker filtering and disabled DHT, peer exchange, and local discovery limit discovery, but the central MonoTorrent seeder does not authenticate incoming peers against the election state. A client that learns its endpoint by another means may connect directly; enforce origin admission at the seeder or network boundary before treating the origin-access rule as a security guarantee.

## Build and configuration

The solution targets .NET 10. A local build requires the .NET 10 SDK and restored dependencies:

```bash
dotnet build BootTorrent.sln
```

Running the applications also requires an MQTT broker. The server uses the `ConnectionStrings:DefaultConnection` setting for SQLite. Server MQTT and torrent settings are under `Mqtt` and `Torrent`; the client uses `Mqtt` and `Client`. The development settings files contain machine-specific paths and loopback addresses, so replace them with paths, broker details, and a tracker URL reachable by the clients. Give every client a unique `Client:ClientIdentifier` and MQTT `Mqtt:ClientId`.

The client project enables Native AOT publishing. Docker and Helm assets are available in the repository, but their deployment behavior is not verified by this README.

## Thesis workspace

The parent workspace contains the current [project knowledge](../Project-Knowledge.md), [research questions](../Research-Questions.md), and [evaluation setup](../Evaluation-Setup.md). Those working documents distinguish intended behavior from code review findings and measured results.
