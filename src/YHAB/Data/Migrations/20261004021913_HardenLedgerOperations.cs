using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class HardenLedgerOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_PlanId_AccountId",
            table: "BudgetTransaction");

        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_PlanId_Date",
            table: "BudgetTransaction");

        migrationBuilder.AddColumn<long>(
            name: "Sequence",
            table: "BudgetTransaction",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "NextSequence",
            table: "BudgetPlans",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "BudgetReceipt",
            columns: table => new
            {
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetReceipt", x => new { x.PlanId, x.OperationId });
                table.ForeignKey(
                    name: "FK_BudgetReceipt_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_AccountId_Date_Sequence",
            table: "BudgetTransaction",
            columns: ["PlanId", "AccountId", "Date", "Sequence"]);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_Date_Sequence_Id",
            table: "BudgetTransaction",
            columns: ["PlanId", "Date", "Sequence", "Id"]);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_Repeat_Date",
            table: "BudgetTransaction",
            columns: ["Repeat", "Date"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BudgetReceipt");

        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_PlanId_AccountId_Date_Sequence",
            table: "BudgetTransaction");

        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_PlanId_Date_Sequence_Id",
            table: "BudgetTransaction");

        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_Repeat_Date",
            table: "BudgetTransaction");

        migrationBuilder.DropColumn(
            name: "Sequence",
            table: "BudgetTransaction");

        migrationBuilder.DropColumn(
            name: "NextSequence",
            table: "BudgetPlans");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_AccountId",
            table: "BudgetTransaction",
            columns: ["PlanId", "AccountId"]);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_Date",
            table: "BudgetTransaction",
            columns: ["PlanId", "Date"]);
    }
}
