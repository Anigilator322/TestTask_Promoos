using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TestTask_Promoos.API;
using TestTask_Promoos.API.Domain;

namespace TestTask_Promoos.Tests;

public class TaskCompletionTests
{
    [Fact]
    public async Task CompleteTask_PublishesTaskCompletedEvent()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17").Build();
        await using var rabbitMq = new RabbitMqBuilder("rabbitmq:4-management")
            .WithUsername("integration").WithPassword("integration-password").Build();
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await Task.WhenAll(postgres.StartAsync(startupTimeout.Token), rabbitMq.StartAsync(startupTimeout.Token));

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TasksDatabase"] = postgres.GetConnectionString(),
                    ["RabbitMQ:HostName"] = rabbitMq.Hostname,
                    ["RabbitMQ:Port"] = rabbitMq.GetMappedPublicPort(5672).ToString(),
                    ["RabbitMQ:UserName"] = "integration",
                    ["RabbitMQ:Password"] = "integration-password",
                    ["RabbitMQ:VirtualHost"] = "/"
                })));
        using var client = factory.CreateClient();

        var connectionFactory = new ConnectionFactory { Uri = new Uri(rabbitMq.GetConnectionString()) };
        await using var connection = await connectionFactory.CreateConnectionAsync(startupTimeout.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: startupTimeout.Token);
        var consumer = new TestConsumer(channel);
        await consumer.StartAsync(startupTimeout.Token);

        using var createdResponse = await client.PostAsJsonAsync("/api/tasks",
            new { title = "Complete integration test", priority = Priority.High });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<TaskItem>();
        Assert.NotNull(created);
        Assert.False(created.IsCompleted);

        using var completedResponse = await client.PutAsync($"/api/tasks/{created.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completedResponse.StatusCode);
        var completed = await completedResponse.Content.ReadFromJsonAsync<TaskItem>();
        Assert.NotNull(completed);
        Assert.True(completed.IsCompleted);
        Assert.NotNull(completed.CompletedAt);

        var received = await consumer.ReceiveAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("task.events", received.Exchange);
        Assert.Equal("task.completed", received.RoutingKey);
        Assert.Equal(created.Id, received.Message.TaskId);
        Assert.Equal(created.Title, received.Message.Title);
        Assert.Equal(completed.CompletedAt.Value, received.Message.CompletedAt);
        Assert.Equal(Priority.High, received.Message.Priority);
    }
}
