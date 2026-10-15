using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using TestTask_Promoos.API.Domain;

namespace TestTask_Promoos.API.Messaging;

public record TaskCompletedEvent(Guid TaskId, string Title, DateTimeOffset CompletedAt, Priority Priority);

public sealed class TaskEventPublisher(
    IConfiguration configuration,
    ILogger<TaskEventPublisher> logger) : BackgroundService
{
    private const string Exchange = "task.events";
    private const string RoutingKey = "task.completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Channel<TaskCompletedEvent> _queue = Channel.CreateBounded<TaskCompletedEvent>(
        new BoundedChannelOptions(1000)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _shutdownDeadline = new();
    private IConnection? _connection;
    private IChannel? _channel;

    public void Enqueue(TaskCompletedEvent message)
    {
        if (!_queue.Writer.TryWrite(message))
            LogUnpublished(message, null, "Publication queue is full or stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        _shutdownDeadline.CancelAfter(ShutdownGracePeriod);
        using var registration = cancellationToken.Register(() => _shutdownDeadline.Cancel());
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stoppingRegistration = stoppingToken.Register(() =>
        {
            _queue.Writer.TryComplete();
            _shutdownDeadline.CancelAfter(ShutdownGracePeriod);
        });
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(_shutdownDeadline.Token))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdownDeadline.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await EnsureChannelAsync(timeout.Token);
                    await _channel!.BasicPublishAsync(
                        exchange: Exchange, routingKey: RoutingKey, mandatory: false,
                        basicProperties: new BasicProperties { ContentType = "application/json", Persistent = true },
                        body: JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions),
                        cancellationToken: timeout.Token);
                }
                catch (Exception exception)
                {
                    LogUnpublished(message, exception, "RabbitMQ publication failed");
                    await DisposeConnectionAsync();
                }
            }
        }
        catch (OperationCanceledException) when (_shutdownDeadline.IsCancellationRequested)
        { }
        finally
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var message))
                LogUnpublished(message, null, "Publisher stopped before publication");
            await DisposeConnectionAsync();
        }
    }

    private async Task EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return;

        await DisposeConnectionAsync();
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMQ:HostName"] ?? "localhost",
            Port = configuration.GetValue("RabbitMQ:Port", 5672),
            UserName = configuration["RabbitMQ:UserName"] ?? "tasks",
            Password = configuration["RabbitMQ:Password"] ?? "tasks_dev_password",
            VirtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
        };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true), cancellationToken);
        await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true,
            autoDelete: false, cancellationToken: cancellationToken);
    }

    private void LogUnpublished(TaskCompletedEvent message, Exception? exception, string reason)
        => logger.LogWarning(exception,
            "{Reason}. Exchange: {Exchange}; RoutingKey: {RoutingKey}; Message: {Message}",
            reason, Exchange, RoutingKey, JsonSerializer.Serialize(message, JsonOptions));

    private async Task DisposeConnectionAsync()
    {
        try
        {
            if (_channel is { IsOpen: true })
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _channel.CloseAsync(timeout.Token);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to dispose RabbitMQ channel");
        }
        finally
        {
            if (_channel is not null)
            {
                try { await _channel.DisposeAsync(); }
                catch (Exception exception) { logger.LogDebug(exception, "Failed to dispose RabbitMQ channel"); }
                _channel = null;
            }
        }

        try
        {
            if (_connection is { IsOpen: true })
                await _connection.CloseAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to dispose RabbitMQ connection");
        }
        finally
        {
            if (_connection is not null)
            {
                try { await _connection.DisposeAsync(); }
                catch (Exception exception) { logger.LogDebug(exception, "Failed to dispose RabbitMQ connection"); }
                _connection = null;
            }
        }
    }
}
