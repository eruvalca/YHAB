using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class PreserveValidCheckpointPublications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BudgetCheckpointInvalidation",
            columns: table => new
            {
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                FirstInvalidMonth = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetCheckpointInvalidation", x => new { x.PlanId, x.Version });
                table.ForeignKey(
                    name: "FK_BudgetCheckpointInvalidation_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BudgetCheckpointInvalidation");
    }
}
