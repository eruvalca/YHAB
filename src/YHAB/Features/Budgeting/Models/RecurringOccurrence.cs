namespace YHAB.Features.Budgeting.Models;

internal sealed record RecurringOccurrence(Guid TemplateId, DateOnly ScheduledDate);
