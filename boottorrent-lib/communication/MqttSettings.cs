namespace boottorrent_lib.communication;

public class MqttSettings
{
    public string ClientId { get; set; }
    public string Server { get; set; }     // IP or hostname
    public int Port { get; set; } = 1883;
    public bool UseTls { get; set; } = false;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool CleanSession { get; set; } = true;
}