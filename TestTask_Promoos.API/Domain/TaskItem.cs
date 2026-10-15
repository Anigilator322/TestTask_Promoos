namespace TestTask_Promoos.API.Domain;

public enum Priority : byte {
    Low = 0, 
    Medium = 1,
    High = 2
}
public class TaskItem
{
    public Guid Id { get; set; }
    public uint RowVersion { get; set; }
    public required string Title { get; set; } // не null, не пустой, максимум 200
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; } // когда завершили
    public Priority Priority { get; set; } // Low, Medium, High
}