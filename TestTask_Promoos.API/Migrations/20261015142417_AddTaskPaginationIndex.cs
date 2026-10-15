using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestTask_Promoos.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPaginationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_Priority_CreatedAt",
                table: "Tasks");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Priority_CreatedAt_Id",
                table: "Tasks",
                columns: new[] { "Priority", "CreatedAt", "Id" },
                descending: new[] { true, false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_Priority_CreatedAt_Id",
                table: "Tasks");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Priority_CreatedAt",
                table: "Tasks",
                columns: new[] { "Priority", "CreatedAt" },
                descending: new[] { true, false });
        }
    }
}
