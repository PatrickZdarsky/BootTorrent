namespace boottorrent_lib.communication;

public enum MqttDirection
{
    Command, // Server ➝ Client
    Event    // Client ➝ Server
}

public enum MqttScope
{
    Global,
    Zone,
    Machine
}

public class MqttTopicContext
{
    public MqttDirection Direction { get; set; }
    public MqttScope Scope { get; set; }
    public string? TargetId { get; set; }       // zoneId or machineId depending on scope
    public string? MessageType { get; set; }

    public static MqttTopicContext CreateEventFromMachine(string machineId, string messageType)
    {
        return new MqttTopicContext
        {
            Direction = MqttDirection.Event,
            Scope = MqttScope.Machine,
            TargetId = machineId,
            MessageType = messageType
        };
    }
    
    public static MqttTopicContext CreateCommandGlobal(string messageType)
    {
        return new MqttTopicContext
        {
            Direction = MqttDirection.Command,
            Scope = MqttScope.Global,
            MessageType = messageType
        };
    }
    
    public static MqttTopicContext Parse(string topic)
    {
        var parts = topic.Split('/');

        if (parts.Length < 3 || parts[0] != "boottorrent")
            throw new ArgumentException("Invalid topic format");

        var dirStr = parts[1];
        var dir = dirStr switch
        {
            "cmd" => MqttDirection.Command,
            "evt" => MqttDirection.Event,
            _ => throw new ArgumentException("Invalid direction in topic")
        };

        if (dir == MqttDirection.Command)
        {
            switch (parts.Length)
            {
                case 4 when parts[2] == "global": // boottorrent/cmd/global/{messageType}
                    return new MqttTopicContext { Direction = dir, Scope = MqttScope.Global, MessageType = parts[3] };
                case 5 when parts[2] == "zone": // boottorrent/cmd/zone/{zoneId}/{messageType}
                    return new MqttTopicContext { Direction = dir, Scope = MqttScope.Zone, TargetId = parts[3], MessageType = parts[4] };
                case 5 when parts[2] == "machine": // boottorrent/cmd/machine/{machineId}/{messageType}
                    return new MqttTopicContext { Direction = dir, Scope = MqttScope.Machine, TargetId = parts[3], MessageType = parts[4] };
            }
        }

        // boottorrent/evt/machine/{machineId}/{messageType}
        if (dir == MqttDirection.Event && parts.Length == 5 && parts[2] == "machine")
        {
            return new MqttTopicContext
            {
                Direction = dir,
                Scope = MqttScope.Machine,
                TargetId = parts[3], // machineId
                MessageType = parts[4]
            };
        }

        throw new ArgumentException("Unsupported topic structure");
    }

    public override string ToString()
    {
        return
            $"{nameof(Direction)}: {Direction}, {nameof(Scope)}: {Scope}, {nameof(TargetId)}: {TargetId}, {nameof(MessageType)}: {MessageType}";
    }
    
    public string ToTopic() 
    {
        var topic = "boottorrent/";

        topic += Direction switch
        {
            MqttDirection.Command => "cmd/",
            MqttDirection.Event => "evt/",
            _ => throw new ArgumentOutOfRangeException()
        };

        topic += Scope switch
        {
            MqttScope.Global => "global/",
            MqttScope.Zone => $"zone/{TargetId}/",
            MqttScope.Machine => $"machine/{TargetId}/",
            _ => throw new ArgumentOutOfRangeException()
        };

        topic += MessageType;

        return topic;
    }
}