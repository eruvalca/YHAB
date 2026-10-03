using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class PreserveRecurringOccurrenceDates : Migration
{
    private static readonly string[] _scheduledColumns = ["PlanId", "SourceTemplateId", "ScheduledDate"];
    private static readonly string[] _dateColumns = ["PlanId", "SourceTemplateId", "Date"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_PlanId_SourceTemplateId_Date",
            table: "BudgetTransaction");

        migrationBuilder.AddColumn<DateOnly>(
            name: "ScheduledDate",
            table: "BudgetTransaction",
            type: "date",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "BudgetTransaction"
            SET "ScheduledDate" = "Date"
            WHERE "SourceTemplateId" IS NOT NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_SourceTemplateId_ScheduledDate",
            table: "BudgetTransaction",
            columns: _scheduledColumns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BudgetTransaction_PlanId_SourceTemplateId_ScheduledDate",
            table: "BudgetTransaction");

        migrationBuilder.DropColumn(
            name: "ScheduledDate",
            table: "BudgetTransaction");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_SourceTemplateId_Date",
            table: "BudgetTransaction",
            columns: _dateColumns,
            unique: true);
    }
}
