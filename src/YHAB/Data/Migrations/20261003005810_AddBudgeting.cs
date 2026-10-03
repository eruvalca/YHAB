using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YHAB.Migrations;

/// <inheritdoc />
internal sealed partial class AddBudgeting : Migration
{
    private static readonly string[] _planIdIdColumns = ["PlanId", "Id"];
    private static readonly string[] _planIdCreditAccountIdColumns = ["PlanId", "CreditAccountId"];
    private static readonly string[] _planIdGroupIdColumns = ["PlanId", "GroupId"];
    private static readonly string[] _planIdPositionColumns = ["PlanId", "Position"];
    private static readonly string[] _planIdCategoryIdColumns = ["PlanId", "CategoryId"];
    private static readonly string[] _planIdTransactionIdColumns = ["PlanId", "TransactionId"];
    private static readonly string[] _planIdAccountIdColumns = ["PlanId", "AccountId"];
    private static readonly string[] _planIdDateColumns = ["PlanId", "Date"];
    private static readonly string[] _planIdSourceTemplateIdDateColumns = ["PlanId", "SourceTemplateId", "Date"];
    private static readonly string[] _planIdTransferAccountIdColumns = ["PlanId", "TransferAccountId"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BudgetPlans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                CreatedOn = table.Column<DateOnly>(type: "date", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                HistoryCursor = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetPlans", x => x.Id);
                table.ForeignKey(
                    name: "FK_BudgetPlans_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BudgetAccount",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                Closed = table.Column<bool>(type: "boolean", nullable: false),
                Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                InterestRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                MinimumPayment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetAccount", x => new { x.PlanId, x.Id });
                table.ForeignKey(
                    name: "FK_BudgetAccount_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BudgetGroup",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Hidden = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetGroup", x => new { x.PlanId, x.Id });
                table.ForeignKey(
                    name: "FK_BudgetGroup_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BudgetHistory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Before = table.Column<string>(type: "jsonb", nullable: false),
                After = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetHistory", x => x.Id);
                table.ForeignKey(
                    name: "FK_BudgetHistory_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BudgetTransaction",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Payee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Memo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                TransferAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                State = table.Column<int>(type: "integer", nullable: false),
                TransferState = table.Column<int>(type: "integer", nullable: false),
                NeedsApproval = table.Column<bool>(type: "boolean", nullable: false),
                Flag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Repeat = table.Column<int>(type: "integer", nullable: false),
                AnchorDate = table.Column<DateOnly>(type: "date", nullable: true),
                Occurrence = table.Column<int>(type: "integer", nullable: false),
                SourceTemplateId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetTransaction", x => new { x.PlanId, x.Id });
                table.ForeignKey(
                    name: "FK_BudgetTransaction_BudgetAccount_PlanId_AccountId",
                    columns: x => new { x.PlanId, x.AccountId },
                    principalTable: "BudgetAccount",
                    principalColumns: _planIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BudgetTransaction_BudgetAccount_PlanId_TransferAccountId",
                    columns: x => new { x.PlanId, x.TransferAccountId },
                    principalTable: "BudgetAccount",
                    principalColumns: _planIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BudgetTransaction_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BudgetCategory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Hidden = table.Column<bool>(type: "boolean", nullable: false),
                CreditAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                TargetKind = table.Column<int>(type: "integer", nullable: true),
                TargetCadence = table.Column<int>(type: "integer", nullable: false),
                TargetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                TargetStartMonth = table.Column<DateOnly>(type: "date", nullable: false),
                TargetDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                TargetRepeatMonths = table.Column<int>(type: "integer", nullable: false),
                TargetWeekday = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetCategory", x => new { x.PlanId, x.Id });
                table.ForeignKey(
                    name: "FK_BudgetCategory_BudgetAccount_PlanId_CreditAccountId",
                    columns: x => new { x.PlanId, x.CreditAccountId },
                    principalTable: "BudgetAccount",
                    principalColumns: _planIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BudgetCategory_BudgetGroup_PlanId_GroupId",
                    columns: x => new { x.PlanId, x.GroupId },
                    principalTable: "BudgetGroup",
                    principalColumns: _planIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BudgetCategory_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BudgetAllocation",
            columns: table => new
            {
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Month = table.Column<DateOnly>(type: "date", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Snoozed = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetAllocation", x => new { x.PlanId, x.CategoryId, x.Month });
                table.ForeignKey(
                    name: "FK_BudgetAllocation_BudgetCategory_PlanId_CategoryId",
                    columns: x => new { x.PlanId, x.CategoryId },
                    principalTable: "BudgetCategory",
                    principalColumns: _planIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BudgetAllocation_BudgetPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "BudgetPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BudgetSplit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Memo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BudgetSplit", x => new { x.PlanId, x.Id });
                table.ForeignKey(
                    name: "FK_BudgetSplit_BudgetCategory_PlanId_CategoryId",
                    columns: x => new { x.PlanId, x.CategoryId },
                    principalTable: "BudgetCategory",
                    principalColumns: _planIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BudgetSplit_BudgetTransaction_PlanId_TransactionId",
                    columns: x => new { x.PlanId, x.TransactionId },
                    principalTable: "BudgetTransaction",
                    principalColumns: _planIdIdColumns,
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BudgetCategory_PlanId_CreditAccountId",
            table: "BudgetCategory",
            columns: _planIdCreditAccountIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetCategory_PlanId_GroupId",
            table: "BudgetCategory",
            columns: _planIdGroupIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetHistory_PlanId_Position",
            table: "BudgetHistory",
            columns: _planIdPositionColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetPlans_OwnerId",
            table: "BudgetPlans",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_BudgetSplit_PlanId_CategoryId",
            table: "BudgetSplit",
            columns: _planIdCategoryIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetSplit_PlanId_TransactionId",
            table: "BudgetSplit",
            columns: _planIdTransactionIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_AccountId",
            table: "BudgetTransaction",
            columns: _planIdAccountIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_Date",
            table: "BudgetTransaction",
            columns: _planIdDateColumns);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_SourceTemplateId_Date",
            table: "BudgetTransaction",
            columns: _planIdSourceTemplateIdDateColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BudgetTransaction_PlanId_TransferAccountId",
            table: "BudgetTransaction",
            columns: _planIdTransferAccountIdColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BudgetAllocation");

        migrationBuilder.DropTable(
            name: "BudgetHistory");

        migrationBuilder.DropTable(
            name: "BudgetSplit");

        migrationBuilder.DropTable(
            name: "BudgetCategory");

        migrationBuilder.DropTable(
            name: "BudgetTransaction");

        migrationBuilder.DropTable(
            name: "BudgetGroup");

        migrationBuilder.DropTable(
            name: "BudgetAccount");

        migrationBuilder.DropTable(
            name: "BudgetPlans");
    }
}
