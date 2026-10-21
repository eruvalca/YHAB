using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class AddBudgetPlanShareCodeRedemptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BudgetPlanShareCodeRedemptions",
            columns: table => new
            {
                CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                RedeemedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetPlanShareCodeRedemptions", x => new { x.CodeHash, x.UserId });
                table.ForeignKey(
                    name: "FK_BudgetPlanShareCodeRedemptions_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BudgetPlanShareCodeRedemptions_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlanShareCodeRedemptions_PlanId",
            table: "BudgetPlanShareCodeRedemptions",
            column: "PlanId");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlanShareCodeRedemptions_RedeemedOn",
            table: "BudgetPlanShareCodeRedemptions",
            column: "RedeemedOn");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlanShareCodeRedemptions_UserId",
            table: "BudgetPlanShareCodeRedemptions",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BudgetPlanShareCodeRedemptions");
    }
}
