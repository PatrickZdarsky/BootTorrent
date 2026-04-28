using System.Collections.Concurrent;
using System.Net;
using btserver.Config;
using MonoTorrent.BEncoding;
using Microsoft.Extensions.Options;

namespace btserver.torrent.tracker;

public class TrackerServer
{
    private readonly HttpListener _listener;
    private readonly string _listeningUri;
    private readonly ConcurrentDictionary<string, Torrent> _torrents = new();
    private readonly ILogger<TrackerServer> _logger;
    private CancellationTokenSource? _cancellationTokenSource;

    private readonly ITorrentArtifactRegistry _artifactRegistry;

    /*
     *  Todo:
     *  - Add a watchdog to time-out peers which haven't announced in a while (e.g. 30 minutes) to prevent stale peers from accumulating.
     *  - Check requests properly (No IPv6, GET, etc.)
     *  - Add ability to add more logic to which peers are returned (for the actual use-case of this project)
     */
    
    public TrackerServer(ILogger<TrackerServer> logger, IOptions<TorrentConfig> settings, ITorrentArtifactRegistry artifactRegistry)
    {
        _logger = logger;
        _artifactRegistry = artifactRegistry;
        _listeningUri = settings.Value.TrackerUrl;
        _listener = new HttpListener();
        _listener.Prefixes.Add(settings.Value.TrackerBindAddress + settings.Value.AnnounceSuffix);
        _listener.Prefixes.Add(settings.Value.TrackerBindAddress + settings.Value.TorrentFileGetSuffix);
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _logger.LogInformation("Listening for requests on {ListeningUri}", _listeningUri);
        Task.Run(() => ListenAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => ProcessRequest(context), cancellationToken);
            }
        }
        catch (HttpListenerException ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "HttpListenerException occurred, listener is stopping.");
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "An unexpected error occurred in the listener loop.");
            }
        }
        finally
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
    }
    
    public void Stop(CancellationToken cancellationToken = default)
    {
        if (_listener.IsListening)
        {
            _logger.LogInformation("Stopping tracker server.");
            _cancellationTokenSource?.Cancel();
            _listener.Stop();
            _listener.Close();
            _cancellationTokenSource?.Dispose();
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.Url is null)
            {
                response.Close();
                return;
            }
            
            if (request.Url.LocalPath.StartsWith("/torrent"))
            {
                //Get torrent hash which is after the /torrent/ suffix
                var torrentHashHex = request.Url.LocalPath.Substring(request.Url.LocalPath.LastIndexOf('/') + 1);
                var artifact = _artifactRegistry.GetArtifactByInfoHash(torrentHashHex);
                if (artifact == null)                {
                    _logger.LogWarning("Torrent file not found for info hash {InfoHash} from {RemoteEndPoint}", torrentHashHex, request.RemoteEndPoint);
                    Error(response, "Torrent file not found.");
                    return;
                }
                
                _logger.LogInformation("Processing torrent get request for info hash {InfoHash}", request.Url.LocalPath);
                response.ContentType = "application/x-bittorrent";
                response.ContentLength64 = artifact.Torrent.TorrentFileBytes.Length;
                response.OutputStream.Write(artifact.Torrent.TorrentFileBytes, 0, artifact.Torrent.TorrentFileBytes.Length);
                response.Close();
                return;
            }
            
            if (!AnnounceRequest.TryParse(request, out var announceRequest))
            {
                _logger.LogWarning("Missing or invalid required parameters from {RemoteEndPoint}", request.RemoteEndPoint);
                Error(response, "Missing or invalid required parameters.");
                return;
            }
            
            ProcessAnnounceRequest(announceRequest, response);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing request from {RemoteEndPoint}", request.RemoteEndPoint);
            Error(response, "Internal server error.");
        }
    }

    private void ProcessAnnounceRequest(AnnounceRequest announceRequest, HttpListenerResponse response)
    {
        var requestingPeer = new Peer(announceRequest.PeerId, new IPEndPoint(announceRequest.RemoteEndPoint.Address, announceRequest.Port), 
            announceRequest.Uploaded, announceRequest.Downloaded, announceRequest.Left);

        var torrent = VerifyAndGetTorrent(announceRequest.InfoHash);
        if (torrent is null)
        {
            Error(response, "Torrent not found.");
            return;
        }

        if (announceRequest.Event == "stopped")
        {
            torrent.RemovePeer(announceRequest.PeerId);
            _logger.LogInformation("Peer {PeerId} stopped.", announceRequest.PeerId);
            response.Close();
            return;
        }
        torrent.UpdatePeer(requestingPeer);
        
        var responseDict = new BEncodedDictionary
        {
            { "interval", new BEncodedNumber(900) },
            { "complete", new BEncodedNumber(torrent.CompletedPeers) },
            { "incomplete", new BEncodedNumber(torrent.IncompletePeers - (requestingPeer.Left > 0 ? 1 : 0)) }
        };

        if (requestingPeer.Left == 0)
        {
            _logger.LogInformation("[{Peer}] is seeding", announceRequest.PeerId);
        }
        else
        {
            var peers = torrent.GetPeers(50); // Get up to 50 peers
            peers.RemoveAll(p => p.PeerId == announceRequest.PeerId); // Don't return the requesting peer
            
            _logger.LogInformation("[{Peer}] Found {PeerCount} peers for torrent {InfoHash} and peer port {Port}", requestingPeer.PeerId, peers.Count, announceRequest.InfoHash, requestingPeer.EndPoint.Port);
            responseDict["peers"] = BuildPeerList(peers, announceRequest.IsCompactRequested);
            
        }

        var responseBytes = responseDict.Encode();
        response.ContentType = "text/plain";
        response.ContentLength64 = responseBytes.Length;
        response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
        response.Close();
    }

    private Torrent? VerifyAndGetTorrent(string announceRequestInfoHash)
    {
        if (_torrents.TryGetValue(announceRequestInfoHash, out var existingTorrent))
            return existingTorrent;
        
        var artifact = _artifactRegistry.GetArtifactByInfoHash(announceRequestInfoHash);
        if (artifact == null)
            return null;
        
        var torrent = new Torrent(artifact.InfoHashV2);
        _torrents[artifact.InfoHashV1] = torrent;
        _torrents[artifact.InfoHashV2] = torrent;
        
        return torrent;
    }

    private static BEncodedValue BuildPeerList(List<Peer> peers, bool isCompactRequested)
    {
        if (isCompactRequested)
        {
            var compactPeers = new byte[peers.Count * 6];
            for (var i = 0; i < peers.Count; i++)
            {
                var addrBytes = peers[i].EndPoint.Address.GetAddressBytes(); // must be 4 bytes for IPv4
                var pport = (ushort) peers[i].EndPoint.Port;

                Buffer.BlockCopy(addrBytes, 0, compactPeers, i * 6, 4);
                compactPeers[i * 6 + 4] = (byte)(pport >> 8);   // network byte order
                compactPeers[i * 6 + 5] = (byte)(pport & 0xff);
            }
            return new BEncodedString(compactPeers);
        }
        
        //Todo: Maybe do not send peer id if "no_peer_id" is set in the request
        //Normal peer list
        return new BEncodedList(peers.Select(p => new BEncodedDictionary
        {
            { "id", new BEncodedString(p.PeerId) },
            { "ip", new BEncodedString(p.EndPoint.Address.ToString()) },
            { "port", new BEncodedNumber(p.EndPoint.Port) }
        }));
    }
    
    private static void Error(HttpListenerResponse response, string message)
    {
        var errorDict = new BEncodedDictionary
        {
            { "failure reason", new BEncodedString(message) }
        };
        var errorBytes = errorDict.Encode();
        response.ContentType = "text/plain";
        response.ContentLength64 = errorBytes.Length;
        response.OutputStream.Write(errorBytes, 0, errorBytes.Length);
        response.Close();
    }
}