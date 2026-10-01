# Proxy election and artifact distribution flow

This document describes the implementation in `btserver`, `btclient`, and `boottorrent-lib`. A **proxy** is an ordinary client designated to discover the central BitTorrent seeder for one artifact. It is not a separate forwarding process. Other clients can exchange pieces with any announced client peer in their distribution zone.

The three independent decisions are:

1. **Membership:** Which management zones contain a machine, and which single distribution zone bounds its peer traffic?
2. **Election:** Which eligible machines in that distribution zone may discover the origin for this artifact?
3. **Discovery:** Which peer addresses does the tracker return for an announce?

## Components and state

| Component | Responsibility |
| --- | --- |
| `ZoneManager` | Resolves all matching management zones and exactly one distribution zone. |
| `MachineRegistry` | Tracks online machines, IPs, heartbeats, completed artifacts, and timeouts. |
| `MachineConfigurationService` | Sends zone configuration and later replays outstanding artifact assignments to a registered client. |
| `ProxyElectionCoordinator` | Signals reconciliation on machine changes and every 30 seconds. |
| `ProxyElectionService` | Reconciles and persists designations for a `(distribution zone, artifact)` pair. |
| `IProxySelectionPolicy` | Orders candidates; the registered `stable-id` policy uses preferred IDs, then machine IDs. |
| `ZoneTorrentAccessPolicy` | Returns zone peers and, for a designated proxy, central seeder peers. |
| `CentralSeederPeerProvider` | Resolves registered central seeder endpoints for the announced info hash. |

SQLite holds these tables:

| Table | Key / contents |
| --- | --- |
| `ProxyZoneConfigurations` | One row per distribution zone: desired initial count, selection policy key, preferred IDs. An absent row means count 1 and `stable-id`. |
| `ProxyElectionStates` | One row per zone and artifact: initialized flag, count last applied, generation. |
| `ProxyDesignations` | The current designated machine IDs, selection time, and reason for each zone and artifact. |
| `ProxyElectionTransitions` | Append-only designation and revocation events, including reason and generation. |
| `DefaultZoneArtifactAssignments` | Artifacts assigned to the computed default distribution zone. |

The old subnet-zone proxy count and IDs are migrated to `ProxyZoneConfigurations` as the initial count and **preferred** IDs. Preferred IDs influence selection order; they are not permanent designations. `Program.cs` applies EF migrations before starting hosted services.

## End-to-end flow

```mermaid
sequenceDiagram
    participant Admin as Management API
    participant DB as SQLite
    participant Server as BTServer
    participant Client as BTClient
    participant Tracker as Tracker
    participant Origin as Central seeder

    Admin->>DB: Configure zone and proxy count
    Client->>Server: MQTT machine started (ID, IP)
    Server->>DB: Load zones
    Server->>Client: MQTT machine configuration (zone IDs, distribution zone, hash)
    Client->>Client: Subscribe to zone MQTT topics
    Client->>Server: Heartbeat with configuration hash and artifact state
    Server->>Client: Replay outstanding artifact jobs when hash matches
    Admin->>DB: Assign artifact to a zone
    Server->>DB: Reconcile proxy designations for distribution zone + artifact
    Server->>Client: MQTT zone artifact assignment
    Client->>Tracker: BitTorrent announce
    Tracker->>DB: Read/reconcile proxy designation
    Tracker-->>Client: Same-zone peers; origin only if designated
    Client->>Origin: Request pieces if designated and connected
```

### 1. Zone resolution and client configuration

`ZoneManager.Resolve` evaluates each persisted `StaticZone` and `SubnetZone` against a machine. **Every matching zone** is a management membership and a corresponding MQTT zone subscription. Among matches with `IsDistributionZone = true`:

- Exactly one match becomes the distribution zone.
- No match gives the machine the computed default distribution zone, ID `7b0a6598-24e7-43a1-844a-a068ce5d19da`. The default ID also joins its management subscriptions.
- Multiple matches leave the distribution zone unset and record the conflicting IDs. The tracker then fails closed for that machine.

`MachineConfigurationService` sends the membership list, distribution zone ID, and configuration hash on registration and after zone edits. The client subscribes and reports the hash in heartbeats. On a matching heartbeat, the server checks persisted artifact assignments in all of that machine's management zones, plus default-zone assignments if applicable, and publishes missing jobs to its machine-scoped MQTT topic. This is the late-registration path.

### 2. Artifact assignment and seeder readiness

`PUT /api/v1/artifacts/{artifactId}/zones/{zoneId}` stores an artifact assignment and publishes an MQTT zone command. The same endpoint accepts the computed default zone ID, storing that assignment in `DefaultZoneArtifactAssignments`. A management-only zone can assign an artifact to its members; proxy election still uses each recipient's **distribution** zone.

The server starts its central MonoTorrent seeder, loads registered artifacts, ensures they are seeding, registers the seeder for lookup, and then starts the tracker. Creating a new artifact also waits for `EnsureSeedingAsync`. Assigning or unassigning an artifact calls election reconciliation before publishing the zone command.

The client ignores duplicate assignments. On restart, persisted artifact metadata is marked initializing until the torrent job is resumed; metadata alone is not reported as a complete copy. On unassignment, the client stops and removes the torrent manager before deleting local files.

### 3. Election

The reconciliation key is `(distributionZoneId, artifactId)`. An eligible candidate must be online, have sent a status heartbeat, resolve unambiguously to that distribution zone, and be assigned the artifact through a matching management zone or the default-zone assignment.

For a new election with at least one eligible machine:

1. If an eligible machine reports the artifact complete, initialize with no origin proxy.
2. Otherwise choose up to `DesiredInitialCount` machines. The `stable-id` policy considers preferred IDs first, then remaining machine IDs in ordinal order.
3. Persist the current designations and a `designated / initial` transition.

Later reconciliations retain eligible incumbents. When one of several proxies disappears, its designation is revoked and the vacant slot remains vacant. When **none** remain, one recovery proxy is selected only if no eligible machine reports a complete copy. A changed configured count is applied explicitly: a decrease removes excess designations; an increase can add candidates if no complete copy is available. A management API request can explicitly set the current designations to distinct, online, assigned machines, up to the configured count.

An assignment with no eligible machines remains uninitialized until a candidate reports status. Reconciliation occurs on machine registration, first/completion-changing heartbeat, stop or timeout, zone edits, artifact assignments, proxy configuration changes, startup, and a 30-second safety sweep. A tracker announce also reconciles its specific zone and artifact before checking designation; announces are not the only election trigger.

### 4. Tracker announce and transfer

The tracker records an announced peer, removes expired peers, and passes the active torrent peers to `TorrentAccessPolicyRegistry`. The registry resolves the requesting machine from its announced IP. If that IP maps to multiple active machines, it cannot identify the requester and returns no zone-aware peers.

`ZoneTorrentAccessPolicy` then:

1. Requires a known requester with exactly one distribution zone and an artifact assigned to that machine.
2. Keeps announced client peers whose IP resolves uniquely to an active machine in the same distribution zone. It excludes the requesting peer.
3. If the requester has a current designation for this artifact, adds registered central seeder peers and reserves room for them within the response limit.
4. Otherwise returns only same-zone client peers. There is no ordinary-client fallback to the central seeder.

The policy is the only registered torrent access policy in normal operation. The `random-fallback` class remains in source but is not registered. The tracker announce interval defaults to 30 seconds. Peer expiry defaults to 120 seconds and is never shorter than twice the announce interval; both values are configurable through `TorrentConfig`. Expiry is applied when the torrent peer set is read.

### 5. Completion, failure, and restart

Client heartbeats carry `LoadedArtifacts` and `PendingArtifacts`. The election service uses an online assigned machine's completed-artifact report to decide whether a replacement needs origin access. A stopped or timed-out proxy is removed from the active registry; the coordinator reconciles and writes revocation/recovery transitions. A zone change can revoke a designation for the same reason.

After a server restart, persisted election state and designations are loaded during reconciliation. Offline or moved machines are removed before tracker access is granted. After a client restart, the machine configuration hash is acknowledged again, outstanding assignments are replayed, and its torrent manager must resume before the client reports the artifact ready.

## Management API

| Method and path | Effect |
| --- | --- |
| `GET /api/v1/policies` | Lists registered torrent access policies. |
| `GET /api/v1/policies/zones/{zoneId}/proxies` | Reads effective configuration, using defaults if no row exists. |
| `PUT /api/v1/policies/zones/{zoneId}/proxies` | Sets `desiredInitialCount`, `selectionPolicyKey` (`stable-id`), and `preferredMachineIds`. |
| `GET /api/v1/policies/zones/{zoneId}/artifacts/{artifactId}/proxies` | Reconciles, then returns current designations. |
| `PUT /api/v1/policies/zones/{zoneId}/artifacts/{artifactId}/proxies` | Sets exact current designations with `machineIds`; each machine must be online, assigned, and in that distribution zone. |
| `GET /api/v1/policies/zones/{zoneId}/artifacts/{artifactId}/proxy-transitions` | Returns the most recent 100 designation and revocation events. |

The older `/api/v1/policies/subnet-zone/...` endpoints were removed. Zone creation and update use `isDistributionZone`; all matching zones remain management memberships.

## Current enforcement boundaries

- The tracker controls **discovery**, not BitTorrent connection admission. DHT, peer exchange, and local peer discovery are disabled for managed torrents, but the central MonoTorrent seeder does not authenticate incoming connections against designations. Someone who learns its endpoint by another route may connect directly. Do not treat tracker results alone as proof that ordinary clients received zero origin payload.
- Client identity at announce time is inferred from IP. Shared or ambiguous IPs fail closed. A stronger machine-to-peer identity binding is needed for NAT or shared-IP deployments.
- The completion decision uses the client's current `Ready` report from MonoTorrent. The server does not independently hash the client's file at election time.
- Configuration, election, and tracker behavior have been checked with a temporary SQLite scenario harness; an end-to-end transfer and packet-path test has not yet been run.

For related policy goals and evaluation scenarios, see [architecture.md](architecture.md) and the [implementation plan](../../Guidance/BootTorrent-implementation-plan.md).
