using MessagePack;

namespace boottorrent_lib.communication.message;

/// <summary>Reports that a torrent reached seeding and its downloaded file matched the expected SHA-256.</summary>
[MessagePackObject]
public class ArtifactDownloadCompletedMessage : IMqttMessage
{
    public static readonly string MessageType = "artifact_download_completed";

    [Key(0)]
    public string ArtifactId { get; set; } = string.Empty;

    [Key(1)]
    public long CompletedAtUnixMilliseconds { get; set; }

    [Key(2)]
    public string FileSha256 { get; set; } = string.Empty;
}
