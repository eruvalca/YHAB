using OneOf;

namespace YHAB.Features.Budgeting.Models;

[GenerateOneOf]
internal sealed partial class CheckpointInvalidation : OneOfBase<PreserveCheckpoints, ResetCheckpoints, InvalidateCheckpointsAfter>;
