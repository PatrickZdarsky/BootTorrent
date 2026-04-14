using System.Security.Cryptography;
using boottorrent_lib.torrent;
using boottorrent_lib.util;
using btserver.settings;
using Microsoft.Extensions.Options;
using MonoTorrent;
using Newtonsoft.Json;

namespace btserver.torrent.monotorrent;

public class MonoTorrentCreator : ITorrentCreator
{
    private ILogger<MonoTorrentCreator> Logger { get; }
    private readonly TorrentSettings _settings;
    
    public MonoTorrentCreator(IOptions<TorrentSettings> settings, ILogger<MonoTorrentCreator> logger)
    {
        Logger = logger;
        this._settings = settings.Value;
    }
    
    public List<TorrentArtifact> LoadedArtifacts { get; } = [];
    
    public async Task LoadExistingArtifactsAsync()
    {
        if (!Directory.Exists(_settings.ArtifactStoragePath))
        {
            Directory.CreateDirectory(_settings.ArtifactStoragePath);
            return;
        }

        LoadedArtifacts.Clear();

        // New layout: each artifact has its own subdirectory containing payload, .torrent and .meta.json.
        var artifactDirectories = Directory.GetDirectories(_settings.ArtifactStoragePath);
        foreach (var artifactDirectory in artifactDirectories)
        {
            var metaFile = Directory.GetFiles(artifactDirectory, "*.meta.json").FirstOrDefault();
            if (metaFile == null)
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(metaFile);
            var artifact = JsonConvert.DeserializeObject<TorrentArtifact>(json);
            if (artifact != null && LoadedArtifacts.All(a => a.ID != artifact.ID))
            {
                LoadedArtifacts.Add(artifact);
            }
        }
    }

    public string ConstructArtifactPathFromArtifact(TorrentArtifact artifact)
    {
        var artifactDirectoryPath = Path.Combine(_settings.ArtifactStoragePath, artifact.ID);
        var artifactFilePath = Directory.GetFiles(artifactDirectoryPath, $"{NameUtil.ToFilePathName(artifact.Name)}.*")
            .FirstOrDefault(f => !f.EndsWith(".torrent") && !f.EndsWith(".meta.json"));
        
        return artifactFilePath ?? throw new FileNotFoundException($"Artifact file for '{artifact.Name}' with ID '{artifact.ID}' not found");
    }

    public string ConstructTorrentPathFromArtifact(TorrentArtifact torrent)
    {        
        var artifactDirectoryPath = Path.Combine(_settings.ArtifactStoragePath, torrent.ID);
        var torrentFilePath = Directory.GetFiles(artifactDirectoryPath, $"{NameUtil.ToFilePathName(torrent.Name)}.torrent").FirstOrDefault();
        
        return torrentFilePath ?? throw new FileNotFoundException($"Torrent file for '{torrent.Name}' with ID '{torrent.ID}' not found");
    }

    public async Task<TorrentArtifact> GenerateTorrentArtifactAsync(string name, string description, string filePath)
    {
        var fileHash = await ComputeSHA256Hash(filePath);
        //Check if we already have this artifact based on the hash
        var existingArtifact = LoadedArtifacts.FirstOrDefault(a => a.IntegritySpec.FileSha256 == fileHash);
        if (existingArtifact != null)
        {
            //Todo: Log that we are reusing existing artifact and the name wasn't updated
            Logger.LogInformation($"Reusing existing artifact {existingArtifact.Name} with ID {existingArtifact.ID} for file {name}");
            return  existingArtifact;
        }
        
        var fileName = NameUtil.ToFilePathName(Path.GetFileName(name));
        var id = Guid.NewGuid().ToString();
        var artifactDirectoryPath = Path.Combine(_settings.ArtifactStoragePath, id);
        Directory.CreateDirectory(artifactDirectoryPath);

        var artifactFilePath = Path.Combine(artifactDirectoryPath, fileName + Path.GetExtension(Path.GetFileName(filePath)));
        var torrentFilePath = Path.Combine(artifactDirectoryPath, fileName + ".torrent");
        var artifactMetaDataPath = Path.Combine(artifactDirectoryPath, fileName + ".meta.json");
        
        //Copy artifact to storage path
        File.Copy(filePath, artifactFilePath, true);
        //Generate torrent file
        var creator = new TorrentCreator();
        creator.Announces.Add([_settings.TrackerUrl]);
        creator.CreatedBy = "BootTorrent Server";
        creator.Name = name;
        creator.Private = true;
        creator.Comment = $"{id}\n{description}";
        creator.PieceLength = _settings.PieceLength;
        
        //Create torrent file
        await using (var torrentFileStream = File.Create(torrentFilePath))
        {
            await creator.CreateAsync(new TorrentFileSource(artifactFilePath), torrentFileStream);
        }
        
        //Load torrent file to get info hash and file bytes
        var torrent = await Torrent.LoadAsync(torrentFilePath);
        var torrentFileBytes = await File.ReadAllBytesAsync(torrentFilePath);

        var torrentArtifact = new TorrentArtifact()
        {
            ID = id,
            Name = name,
            Torrent = new TorrentDescriptor()
            {
                InfoHashV1 = torrent.InfoHashes.V1!.ToHex(),
                InfoHashV2 = torrent.InfoHashes.V2!.ToHex(),
                TorrentFileBytes = torrentFileBytes
            },
            IntegritySpec = new IntegritySpec()
            {
                FileSha256 = fileHash
            }
        };

        //Save metadata
        await File.WriteAllTextAsync(artifactMetaDataPath, JsonConvert.SerializeObject(torrentArtifact));

        LoadedArtifacts.Add(torrentArtifact);
        
        return torrentArtifact;
    }

    private async Task<string> ComputeSHA256Hash(string filePath)
    {
        var cancellationToken = CancellationToken.None; // Some cancellation token
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 4096, useAsync: true);
        using (var sha256 = SHA256.Create())
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
            sha256.TransformFinalBlock(buffer, 0, 0);
            return Convert.ToHexStringLower(sha256.Hash!);
        }
    }
}