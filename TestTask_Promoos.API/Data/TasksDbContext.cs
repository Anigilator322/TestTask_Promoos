using Microsoft.EntityFrameworkCore;
using TestTask_Promoos.API.Domain;

namespace TestTask_Promoos.API.Data;

public class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(task => task.Id);
            // Npgsql maps uint row versions to PostgreSQL's automatically updated xmin.
            entity.Property(task => task.RowVersion).IsRowVersion();
            entity.Property(task => task.Title).IsRequired().HasMaxLength(200);
            entity.HasIndex(task => new { task.Priority, task.CreatedAt, task.Id })
                .IsDescending(true, false, false);
        });
    }
}
