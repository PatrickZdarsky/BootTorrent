BootTorrent MQTT Setup
======================
## Encoding
For efficient Messages [MessagePack](https://msgpack.org/) will be used.
## Topics
### [Server>] Global 'boottorrent'
Used by the server to adress all machines.
### [Server>] Zone 'boottorrent/zone/{zoneId}'
Used by the server to communicate with a whole zone (group of machines)
### [Server>] Machine 'boottorrent/machine/{machineId}'
Used by the server to communicate with a single machine

### [Client>] Machine Feedback 'boottorrent/machine/{machineId}/feedback'
Used by machines to send events to the server