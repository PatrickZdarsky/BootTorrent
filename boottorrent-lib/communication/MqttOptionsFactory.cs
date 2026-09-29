using MQTTnet;
using MQTTnet.Formatter;

namespace boottorrent_lib.communication;

public static class MqttOptionsFactory
{
    public static MqttClientOptions Create(MqttSettings settings)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(settings.ClientId)
            .WithTcpServer(settings.Server, settings.Port)
            .WithCleanSession(settings.CleanSession)
            .WithProtocolVersion(MqttProtocolVersion.V311);

        if (!string.IsNullOrEmpty(settings.Username))
        {
            builder = builder.WithCredentials(settings.Username, settings.Password);
        }

        if (settings.UseTls)
        {
            builder = builder.WithTlsOptions(o => o.UseTls());
        }

        return builder.Build();
    }
}