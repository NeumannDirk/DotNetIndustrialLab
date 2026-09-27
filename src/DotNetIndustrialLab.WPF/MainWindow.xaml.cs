using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace DotNetIndustrialLab.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string DefaultBrokerHost = "localhost";
    private const int DefaultBrokerPort = 1883;
    private const string DefaultTopic = "industrial-lab/sensors/temperature";

    private readonly IMqttClient _mqttClient;
    private readonly CancellationTokenSource _shutdown = new();
    private string _temperatureText = "Waiting for sensor value...";
    private string _connectionStatus = "Connecting to MQTT broker...";

    public string TemperatureText
    {
        get => _temperatureText;
        set
        {
            if (_temperatureText != value)
            {
                _temperatureText = value;
                OnPropertyChanged();
            }
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            if (_connectionStatus != value)
            {
                _connectionStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _mqttClient = new MqttFactory().CreateMqttClient();
        _mqttClient.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        Closed += OnClosed;

        _ = ConnectToMqttAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task ConnectToMqttAsync()
    {
        var brokerHost = Environment.GetEnvironmentVariable("MQTT_BROKER") ?? DefaultBrokerHost;
        var brokerPort = GetEnvironmentInt("MQTT_PORT") ?? DefaultBrokerPort;
        var topic = Environment.GetEnvironmentVariable("MQTT_TOPIC") ?? DefaultTopic;

        try
        {
            var clientOptions = new MqttClientOptionsBuilder()
                .WithClientId($"dotnet-industrial-lab-wpf-{Guid.NewGuid():N}")
                .WithTcpServer(brokerHost, brokerPort)
                .Build();

            await _mqttClient.ConnectAsync(clientOptions, _shutdown.Token);
            await _mqttClient.SubscribeAsync(
                new MqttTopicFilterBuilder()
                    .WithTopic(topic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                _shutdown.Token);

            ConnectionStatus = $"Connected to {brokerHost}:{brokerPort}; subscribed to {topic}";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ConnectionStatus = $"MQTT connection failed: {exception.Message}";
        }
    }

    private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        try
        {
            var payload = Encoding.UTF8.GetString(eventArgs.ApplicationMessage.PayloadSegment);
            var sensorValue = JsonSerializer.Deserialize<SensorValue>(payload);

            if (sensorValue is null)
            {
                Dispatcher.Invoke(() => ConnectionStatus = "Received an empty sensor value.");
                return Task.CompletedTask;
            }

            Dispatcher.Invoke(() =>
            {
                TemperatureText = $"{sensorValue.Value:F2} {sensorValue.Unit}";
                ConnectionStatus = $"Last update: {sensorValue.TimestampUtc.LocalDateTime:G}";
            });
        }
        catch (JsonException exception)
        {
            Dispatcher.Invoke(() => ConnectionStatus = $"Invalid sensor payload: {exception.Message}");
        }

        return Task.CompletedTask;
    }

    private async void OnClosed(object? sender, EventArgs eventArgs)
    {
        _shutdown.Cancel();

        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync();
        }

        _mqttClient.Dispose();
        _shutdown.Dispose();
    }

    private static int? GetEnvironmentInt(string name)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var result) ? result : null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record SensorValue(
        string SensorId,
        double Value,
        string Unit,
        DateTimeOffset TimestampUtc);
}