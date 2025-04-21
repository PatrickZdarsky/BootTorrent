using MQTTnet.Client;

namespace boottorrent_lib.communication;

public static class MqttOptionsFactory
{
    public static MqttClientOptions Create(MqttSettings settings)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(settings.ClientId)
            .WithTcpServer(settings.Server, settings.Port)
            .WithCleanSession(settings.CleanSession);

        if (!string.IsNullOrEmpty(settings.Username))
        {
            builder = builder.WithCredentials(settings.Username, settings.Password);
        }

        if (settings.UseTls)
        {
            builder = builder.WithTls();
        }

        return builder.Build();
    }
}