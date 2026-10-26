using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class AddBudgetPlanShareCodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BudgetPlanShareCodes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExpiresOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetPlanShareCodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_BudgetPlanShareCodes_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlanShareCodes_CodeHash",
            table: "BudgetPlanShareCodes",
            column: "CodeHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlanShareCodes_ExpiresOn",
            table: "BudgetPlanShareCodes",
            column: "ExpiresOn");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlanShareCodes_PlanId",
            table: "BudgetPlanShareCodes",
            column: "PlanId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BudgetPlanShareCodes");
    }
}
