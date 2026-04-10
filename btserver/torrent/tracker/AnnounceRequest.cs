using System.Net;
using btserver.torrent.impl;

namespace btserver.torrent.tracker;

public class AnnounceRequest
{
    public string InfoHash { get; }
    public string PeerId { get; }
    public int Port { get; }
    public long Uploaded { get; }
    public long Downloaded { get; }
    public long Left { get; }
    public string? Event { get; }
    public bool IsCompactRequested { get; }
    public IPEndPoint RemoteEndPoint { get; }

    private AnnounceRequest(string infoHash, string peerId, int port, long uploaded, long downloaded, long left, string? eventStr, bool isCompactRequested, IPEndPoint remoteEndPoint)
    {
        InfoHash = infoHash;
        PeerId = peerId;
        Port = port;
        Uploaded = uploaded;
        Downloaded = downloaded;
        Left = left;
        Event = eventStr;
        IsCompactRequested = isCompactRequested;
        RemoteEndPoint = remoteEndPoint;
    }

    public static bool TryParse(HttpListenerRequest request, out AnnounceRequest? announceRequest)
    {
        announceRequest = null;
        var query = request.QueryString;

        var infoHashBytes = TorrentQueryParser.ExtractInfoHashFromRawUrl(request.RawUrl!);
        var infoHash = Convert.ToHexString(infoHashBytes);
        var peerId = query["peer_id"];
        var portStr = query["port"];
        var uploadedStr = query["uploaded"];
        var downloadedStr = query["downloaded"];
        var leftStr = query["left"];
        var eventStr = query["event"];
        var isCompactRequested = query["compact"] == "1";

        if (string.IsNullOrEmpty(infoHash) || string.IsNullOrEmpty(peerId) || !int.TryParse(portStr, out var port) ||
            !long.TryParse(uploadedStr, out var uploaded) || !long.TryParse(downloadedStr, out var downloaded) || !long.TryParse(leftStr, out var left))
        {
            return false;
        }

        announceRequest = new AnnounceRequest(infoHash, peerId, port, uploaded, downloaded, left, eventStr, isCompactRequested, request.RemoteEndPoint);
        return true;
    }
}
