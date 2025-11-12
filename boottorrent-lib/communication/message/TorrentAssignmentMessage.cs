using boottorrent_lib.torrent;
using MessagePack;

namespace boottorrent_lib.communication.message;

public class TorrentAssignmentMessage : IMqttMessage
{
    public static readonly string MessageType = "torrent_assignment";

    [Key(0)]
    public TorrentJob TorrentJob { get; set; }
    
}