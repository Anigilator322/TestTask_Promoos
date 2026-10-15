using Microsoft.EntityFrameworkCore;
using TestTask_Promoos.API.Messaging;
using TestTask_Promoos.API.Data;
using TestTask_Promoos.API.Features;

namespace TestTask_Promoos.API;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddDbContext<TasksDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("TasksDatabase")));
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<TaskEventPublisher>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<TaskEventPublisher>());

        var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
        }
        if (app.Environment.IsDevelopment())
            app.MapOpenApi();
        app.MapTasks();
        await app.RunAsync();
    }
}