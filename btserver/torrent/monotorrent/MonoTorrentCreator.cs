using boottorrent_lib.torrent;
using boottorrent_lib.util;
using btserver.settings;
using Microsoft.Extensions.Options;
using MonoTorrent;

namespace btserver.torrent.monotorrent;

public class MonoTorrentCreator : ITorrentCreator
{
    public readonly ArtifactSettings settings;
    
    public MonoTorrentCreator(IOptions<ArtifactSettings> settings)
    {
        this.settings = settings.Value;
    }
    
    
    public async Task<TorrentArtifact> GenerateTorrentArtifactAsync(string name, string description, string filePath)
    {
        string fileName = NameUtil.ToFilePathName(Path.GetFileName(filePath));
        string artifactFilePath = Path.Combine(settings.ArtifactStoragePath, fileName + Path.GetExtension(Path.GetFileName(filePath)));
        string torrentFilePath = Path.Combine(settings.ArtifactStoragePath, fileName + ".torrent");
        string artifactMetaDataPath = Path.Combine(settings.ArtifactStoragePath, fileName + ".meta.json");
        
        //Copy artifact to storage path
        File.Copy(filePath, artifactFilePath, true);
        //Generate torrent file
        var creator = new MonoTorrent.TorrentCreator();
        creator.Announces.Add(new List<string> { settings.TrackerUrl });
        creator.CreatedBy = "BootTorrent Server";
        creator.Name = fileName;
        creator.Comment = description;
        creator.PieceLength = settings.PieceLength;
        
        //Create torrent file
        await using var torrentFileStream = File.Create(torrentFilePath);
        await creator.CreateAsync(new TorrentFileSource(artifactFilePath), torrentFileStream);
        
        //Load torrent file to get info hash
        var torrent = await Torrent.LoadAsync(torrentFilePath);
        
        //Create TorrentArtifact and save as metadata
        // var torrentArtifact = new TorrentArtifact
        // {
        //     Name = fileName,
        //     torrent = new TorrentDescriptor
        //     {
        //         InfoHash = torrent.InfoHashes.V2,
        //         Name = fileName,
        //         SizeBytes = new FileInfo(artifactFilePath).Length,
        //         TorrentUrl = $"{settings.ServerBaseUrl}/artifacts/{fileName}.torrent",
        //         TrackerUrl = settings.TrackerUrl,
        //         IsPrivate = settings.IsPrivate
        //     },
        //     integritySpec = new IntegritySpec
        //     {
        //         PieceLength = settings.PieceLength,
        //         HashAlgorithm = "SHA1"
        //     }
        // };

        return null;
    }
}