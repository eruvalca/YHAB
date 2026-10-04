using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class AddBudgetCheckpoints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BudgetCheckpoint",
            columns: table => new
            {
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                FormatVersion = table.Column<int>(type: "integer", nullable: false),
                State = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetCheckpoint", x => new { x.PlanId, x.Month });
                table.ForeignKey(
                    name: "FK_BudgetCheckpoint_BudgetPlans_PlanId",
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
            name: "BudgetCheckpoint");
    }
}
