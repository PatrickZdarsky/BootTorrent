namespace btclient.torrent;

/// <summary>Closes existing torrent connections after origin access is revoked.</summary>
public interface IOriginPeerConnectionController
{
    Task ResetConnectionsAsync();
}
