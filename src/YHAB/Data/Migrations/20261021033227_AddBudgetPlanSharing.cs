using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class AddBudgetPlanSharing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BudgetPlanMembers",
            columns: table => new
            {
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetPlanMembers", x => new { x.PlanId, x.UserId });
                table.ForeignKey(
                    name: "FK_BudgetPlanMembers_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BudgetPlanMembers_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlanMembers_UserId",
            table: "BudgetPlanMembers",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BudgetPlanMembers");
    }
}
