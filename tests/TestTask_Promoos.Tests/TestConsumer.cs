using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TestTask_Promoos.API.Messaging;

namespace TestTask_Promoos.Tests;

internal sealed class TestConsumer(IChannel channel)
{
    private readonly TaskCompletionSource<ReceivedEvent> _received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync("task.events", ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);
        var queue = await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true,
            autoDelete: true, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue.QueueName, "task.events", "task.completed",
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) =>
        {
            try
            {
                // Deserialize inside the callback: RabbitMQ owns the body buffer.
                var message = JsonSerializer.Deserialize<TaskCompletedEvent>(args.Body.Span,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new JsonException("Expected a task.completed event.");
                _received.TrySetResult(new ReceivedEvent(args.Exchange, args.RoutingKey, message));
            }
            catch (Exception exception)
            {
                _received.TrySetException(exception);
            }
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer: consumer,
            cancellationToken: cancellationToken);
    }

    public Task<ReceivedEvent> ReceiveAsync(TimeSpan timeout) => _received.Task.WaitAsync(timeout);

    internal sealed record ReceivedEvent(string Exchange, string RoutingKey, TaskCompletedEvent Message);
}
