using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowInstanceRunningUniquePerEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_WorkflowInstances_Binding_EntityKey_Running",
                table: "WorkflowInstances",
                column: "EntityKey",
                unique: true,
                filter: "[EntityKey] IS NOT NULL AND [WorkflowBindingId] IS NOT NULL AND [Status] = N'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WorkflowInstances_Binding_EntityKey_Running",
                table: "WorkflowInstances");
        }
    }
}
