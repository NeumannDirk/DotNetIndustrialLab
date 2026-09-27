using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

const string defaultBrokerHost = "localhost";
const int defaultBrokerPort = 1883;
const string defaultTopic = "industrial-lab/sensors/temperature";
const int defaultIntervalSeconds = 1;

var brokerHost = GetArgument(args, "--broker") ?? Environment.GetEnvironmentVariable("MQTT_BROKER") ?? defaultBrokerHost;
var brokerPort = GetIntArgument(args, "--port")
    ?? GetEnvironmentInt("MQTT_PORT")
    ?? defaultBrokerPort;
var topic = GetArgument(args, "--topic") ?? Environment.GetEnvironmentVariable("MQTT_TOPIC") ?? defaultTopic;
var intervalSeconds = GetIntArgument(args, "--interval")
    ?? GetEnvironmentInt("MQTT_INTERVAL_SECONDS")
    ?? defaultIntervalSeconds;

if (brokerPort is < 1 or > 65535)
{
    Console.Error.WriteLine("The MQTT broker port must be between 1 and 65535.");
    return 1;
}

if (intervalSeconds < 1)
{
    Console.Error.WriteLine("The publish interval must be at least one second.");
    return 1;
}

var mqttFactory = new MqttFactory();
using var mqttClient = mqttFactory.CreateMqttClient();
using var cancellationSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var clientOptions = new MqttClientOptionsBuilder()
    .WithClientId($"dotnet-industrial-lab-mock-{Guid.NewGuid():N}")
    .WithTcpServer(brokerHost, brokerPort)
    .Build();

try
{
    Console.WriteLine($"Connecting to MQTT broker {brokerHost}:{brokerPort}...");
    await mqttClient.ConnectAsync(clientOptions, cancellationSource.Token);
    Console.WriteLine($"Publishing sensor values to '{topic}' every {intervalSeconds} second(s).");
    Console.WriteLine("Press Ctrl+C to stop.");

    var random = new Random();
    while (!cancellationSource.Token.IsCancellationRequested)
    {
        var sensorValue = new SensorValue(
            SensorId: "temperature-01",
            Value: Math.Round(18 + random.NextDouble() * 8, 2),
            Unit: "°C",
            TimestampUtc: DateTimeOffset.UtcNow);

        var payload = JsonSerializer.Serialize(sensorValue);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await mqttClient.PublishAsync(message, cancellationSource.Token);
        Console.WriteLine($"{sensorValue.TimestampUtc:O} {sensorValue.SensorId}: {sensorValue.Value:F2} {sensorValue.Unit}");

        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationSource.Token);
    }
}
catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
{
}
catch (Exception exception)
{
    Console.Error.WriteLine($"The MQTT mock could not connect or stopped unexpectedly: {exception.Message}");
    return 1;
}
finally
{
    if (mqttClient.IsConnected)
    {
        await mqttClient.DisconnectAsync();
    }
}

return 0;

static string? GetArgument(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static int? GetIntArgument(string[] args, string name)
{
    var value = GetArgument(args, name);
    return int.TryParse(value, out var result) ? result : null;
}

static int? GetEnvironmentInt(string name)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var result) ? result : null;
}

file record SensorValue(
    string SensorId,
    double Value,
    string Unit,
    DateTimeOffset TimestampUtc);
