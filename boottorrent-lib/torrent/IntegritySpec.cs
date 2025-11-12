namespace boottorrent_lib.torrent;

public class IntegritySpec
{
    public string FileSha256 { get; init; } = default!;
    
    //Maybe add a signature or something later
}