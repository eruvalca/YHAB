using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class RemoveTransactionStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NeedsApproval",
            table: "BudgetTransaction");

        migrationBuilder.DropColumn(
            name: "State",
            table: "BudgetTransaction");

        migrationBuilder.DropColumn(
            name: "TransferState",
            table: "BudgetTransaction");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "NeedsApproval",
            table: "BudgetTransaction",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "State",
            table: "BudgetTransaction",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "TransferState",
            table: "BudgetTransaction",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }
}
