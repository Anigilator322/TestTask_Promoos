using TestTask_Promoos.API.Messaging;
using Microsoft.EntityFrameworkCore;
using TestTask_Promoos.API.Data;
using TestTask_Promoos.API.Domain;

namespace TestTask_Promoos.API.Features;

public static class TasksEndpointExtensions
{
    public static void MapTasks(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/api/tasks");
        tasks.MapPost("", PostTask);
        tasks.MapGet("", GetTasks);
        tasks.MapPut("/{id:guid}/complete", CompleteTask);
        tasks.MapDelete("/{id:guid}", DeleteTask);
    }

    private static async Task<IResult> PostTask(
        CreateTaskRequest request, 
        TasksDbContext db, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            return Results.BadRequest();

        if (!Enum.IsDefined(request.Priority))
            return Results.BadRequest();
        
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Priority = request.Priority,
            IsCompleted =  request.IsCompleted,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/tasks/{task.Id}", task);
    }

    private static async Task<IResult> GetTasks(
        TasksDbContext db,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return Results.BadRequest(new { error = "page must be >= 1; pageSize must be between 1 and 100." });

        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            return Results.BadRequest(new { error = "Requested page offset is too large." });

        var query = db.Tasks.AsNoTracking();
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.CreatedAt)
            .ThenBy(task => task.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Results.Ok(new TaskPage(items, page, pageSize, totalCount,
            totalCount / pageSize + (totalCount % pageSize == 0 ? 0 : 1)));
    }

    private static async Task<IResult> CompleteTask(
        Guid id, TasksDbContext db, TaskEventPublisher publisher, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        if (task is null)
            return Results.NotFound();
        if (task.IsCompleted)
            return Results.Conflict();

        task.IsCompleted = true;
        task.CompletedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict();
        }

        publisher.Enqueue(new TaskCompletedEvent(task.Id, task.Title, task.CompletedAt.Value, task.Priority));
        return Results.Ok(task);
    }

    private static async Task<IResult> DeleteTask(
        Guid id, TasksDbContext db, CancellationToken cancellationToken)
    {
        var deleted = await db.Tasks.Where(task => task.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }
}

public record CreateTaskRequest(string? Title, Priority Priority = Priority.Medium, bool IsCompleted = false);

public record TaskPage(IReadOnlyList<TaskItem> Items, int Page, int PageSize, long TotalCount, long TotalPages);
