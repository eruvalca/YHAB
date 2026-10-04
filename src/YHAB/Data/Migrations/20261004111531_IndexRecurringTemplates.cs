using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class IndexRecurringTemplates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_Repeat_Date",
            table: "BudgetTransaction");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_RecurringTemplates",
            table: "BudgetTransaction",
            columns: ["PlanId", "Date", "Sequence", "Id"],
            filter: "\"Repeat\" <> 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_RecurringTemplates",
            table: "BudgetTransaction");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_Repeat_Date",
            table: "BudgetTransaction",
            columns: ["Repeat", "Date"]);
    }
}
