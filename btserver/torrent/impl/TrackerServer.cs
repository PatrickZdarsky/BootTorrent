using System.Net;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using MonoTorrent.BEncoding;

namespace btserver.torrent.impl;

public class TrackerServer
{
    private readonly HttpListener _listener;
    private readonly string _listeningUri;
    private readonly ConcurrentDictionary<string, Torrent> _torrents = new();
    private readonly ILogger<TrackerServer> _logger;

    public TrackerServer(ILogger<TrackerServer> logger, string listeningUri = "http://localhost:6969/announce/")
    {
        _logger = logger;
        _listeningUri = listeningUri;
        _listener = new HttpListener();
        _listener.Prefixes.Add(_listeningUri);
    }

    public async Task Start(CancellationToken cancellationToken = default)
    {
        _listener.Start();
        _logger.LogInformation("Listening for requests on {ListeningUri}", _listeningUri);

        try
        {
            while (_listener.IsListening)
            {
                var context = await _listener.GetContextAsync();
                // In the next step, we will process the request here.
                ProcessRequest(context);
            }
        }
        catch (HttpListenerException ex)
        {
            _logger.LogWarning(ex, "HttpListenerException occurred, listener is stopping.");
        }
    }
    
    public async Task Stop(CancellationToken cancellationToken = default)
    {
        if (_listener.IsListening)
        {
            _logger.LogInformation("Stopping tracker server.");
            _listener.Stop();
            _listener.Close();
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        
        _logger.LogDebug("Processing request from {RemoteEndPoint}", request.RemoteEndPoint);

        try
        {
            var query = request.QueryString;

            var infoHash = query["info_hash"];
            var peerId = query["peer_id"];
            var portStr = query["port"];
            var uploadedStr = query["uploaded"];
            var downloadedStr = query["downloaded"];
            var leftStr = query["left"];
            var eventStr = query["event"];

            _logger.LogDebug("Request parameters: info_hash={InfoHash}, peer_id={PeerId}, port={Port}, uploaded={Uploaded}, downloaded={Downloaded}, left={Left}, event={Event}", 
                infoHash, peerId, portStr, uploadedStr, downloadedStr, leftStr, eventStr);

            if (string.IsNullOrEmpty(infoHash) || string.IsNullOrEmpty(peerId) || !int.TryParse(portStr, out var port) ||
                !long.TryParse(uploadedStr, out var uploaded) || !long.TryParse(downloadedStr, out var downloaded) || !long.TryParse(leftStr, out var left))
            {
                _logger.LogWarning("Missing or invalid required parameters from {RemoteEndPoint}", request.RemoteEndPoint);
                Error(response, "Missing or invalid required parameters.");
                return;
            }
            
            var remoteEndpoint = request.RemoteEndPoint;
            var peer = new Peer(peerId, new IPEndPoint(remoteEndpoint.Address, port), uploaded, downloaded, left);

            var torrent = _torrents.GetOrAdd(infoHash, new Torrent(infoHash));

            if (eventStr == "stopped")
            {
                torrent.RemovePeer(peerId);
                _logger.LogInformation("Peer {PeerId} stopped.", peerId);
            }
            else
            {
                torrent.UpdatePeer(peer);
                _logger.LogInformation("Peer {PeerId} updated.", peerId);
            }

            var peers = torrent.GetPeers(50); // Get up to 50 peers
            _logger.LogDebug("Found {PeerCount} peers for torrent {InfoHash}", peers.Count, infoHash);

            var responseDict = new BEncodedDictionary
            {
                { "interval", new BEncodedNumber(1800) }, // 30 minutes
                { "peers", new BEncodedList(peers.Select(p => new BEncodedDictionary
                {
                    { "peer id", new BEncodedString(p.PeerId) },
                    { "ip", new BEncodedString(p.EndPoint.Address.ToString()) },
                    { "port", new BEncodedNumber(p.EndPoint.Port) }
                })) }
            };

            var responseBytes = responseDict.Encode();
            response.ContentType = "text/plain";
            response.ContentLength64 = responseBytes.Length;
            response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            response.Close();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing request from {RemoteEndPoint}", request.RemoteEndPoint);
            Error(response, "Internal server error.");
        }
    }
    
    private void Error(HttpListenerResponse response, string message)
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