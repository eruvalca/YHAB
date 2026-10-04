using Microsoft.EntityFrameworkCore;
using YHAB.Data;

namespace YHAB.Features.Budgeting.Data;

internal static class BudgetModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        var plan = builder.Entity<BudgetPlan>();
        plan.ToTable("BudgetPlans");
        plan.HasKey(item => item.Id);
        plan.Property(item => item.Name).HasMaxLength(100);
        plan.Property(item => item.Notes).HasMaxLength(4000);
        plan.Property(item => item.Version).IsConcurrencyToken();
        plan.HasIndex(item => item.OwnerId);
        plan.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Cascade);

        var account = builder.Entity<BudgetAccount>();
        account.HasKey(item => new { item.PlanId, item.Id });
        account.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        account.Property(item => item.Name).HasMaxLength(100);
        account.Property(item => item.Notes).HasMaxLength(4000);

        var group = builder.Entity<BudgetGroup>();
        group.HasKey(item => new { item.PlanId, item.Id });
        group.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        group.Property(item => item.Name).HasMaxLength(100);

        var category = builder.Entity<BudgetCategory>();
        category.HasKey(item => new { item.PlanId, item.Id });
        category.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        category.HasOne<BudgetGroup>().WithMany().HasForeignKey(item => new { item.PlanId, item.GroupId }).OnDelete(DeleteBehavior.Restrict);
        category.HasOne<BudgetAccount>().WithMany().HasForeignKey(item => new { item.PlanId, item.CreditAccountId }).OnDelete(DeleteBehavior.Restrict);
        category.Property(item => item.Name).HasMaxLength(100);
        category.Property(item => item.Notes).HasMaxLength(4000);
        category.HasIndex(item => new { item.PlanId, item.CreditAccountId }).IsUnique();

        var allocation = builder.Entity<BudgetAllocation>();
        allocation.HasKey(item => new { item.PlanId, item.CategoryId, item.Month });
        allocation.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        allocation.HasOne<BudgetCategory>().WithMany().HasForeignKey(item => new { item.PlanId, item.CategoryId }).OnDelete(DeleteBehavior.Restrict);

        var entry = builder.Entity<BudgetTransaction>();
        entry.HasKey(item => new { item.PlanId, item.Id });
        entry.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        entry.HasOne<BudgetAccount>().WithMany().HasForeignKey(item => new { item.PlanId, item.AccountId }).OnDelete(DeleteBehavior.Restrict);
        entry.HasOne<BudgetAccount>().WithMany().HasForeignKey(item => new { item.PlanId, item.TransferAccountId }).OnDelete(DeleteBehavior.Restrict);
        entry.HasIndex(item => new { item.PlanId, item.Date, item.Sequence, item.Id });
        entry.HasIndex(item => new { item.PlanId, item.AccountId, item.Date, item.Sequence });
        // Both per-plan posting and global worker discovery ignore posted entries.
        entry.HasIndex(item => new { item.PlanId, item.Date, item.Sequence, item.Id }, "IX_BudgetTransaction_RecurringTemplates")
            .HasFilter("\"Repeat\" <> 0");
        entry.HasIndex(item => new { item.PlanId, item.SourceTemplateId, item.ScheduledDate }).IsUnique();
        entry.Property(item => item.Payee).HasMaxLength(200);
        entry.Property(item => item.Memo).HasMaxLength(4000);
        entry.Property(item => item.Flag).HasMaxLength(20);

        var split = builder.Entity<BudgetSplit>();
        split.HasKey(item => new { item.PlanId, item.Id });
        split.HasOne<BudgetTransaction>().WithMany().HasForeignKey(item => new { item.PlanId, item.TransactionId }).OnDelete(DeleteBehavior.Cascade);
        split.HasOne<BudgetCategory>().WithMany().HasForeignKey(item => new { item.PlanId, item.CategoryId }).OnDelete(DeleteBehavior.Restrict);
        split.Property(item => item.Memo).HasMaxLength(1000);

        var history = builder.Entity<BudgetHistory>();
        history.HasKey(item => item.Id);
        history.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        history.HasIndex(item => new { item.PlanId, item.Position }).IsUnique();
        history.Property(item => item.Description).HasMaxLength(200);
        history.Property(item => item.Before).HasColumnType("jsonb");
        history.Property(item => item.After).HasColumnType("jsonb");

        var receipt = builder.Entity<BudgetReceipt>();
        receipt.HasKey(item => new { item.PlanId, item.OperationId });
        receipt.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        receipt.Property(item => item.RequestHash).HasMaxLength(64);

        var checkpoint = builder.Entity<BudgetCheckpoint>();
        checkpoint.HasKey(item => new { item.PlanId, item.Month });
        checkpoint.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);
        checkpoint.Property(item => item.State).HasColumnType("jsonb");

        var invalidation = builder.Entity<BudgetCheckpointInvalidation>();
        invalidation.HasKey(item => new { item.PlanId, item.Version });
        invalidation.HasOne<BudgetPlan>().WithMany().HasForeignKey(item => item.PlanId);

        foreach (var entity in builder.Model.GetEntityTypes().Where(item => string.Equals(item.ClrType.Namespace, typeof(BudgetPlan).Namespace, StringComparison.Ordinal)))
        {
            foreach (var property in entity.GetProperties().Where(item => item.ClrType == typeof(decimal)))
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }
        }
    }
}
