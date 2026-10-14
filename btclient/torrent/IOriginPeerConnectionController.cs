namespace btclient.torrent;

/// <summary>Updates torrent peers when origin access changes.</summary>
public interface IOriginPeerConnectionController
{
    Task RefreshTrackerAsync();
    Task ResetConnectionsAsync();
}
