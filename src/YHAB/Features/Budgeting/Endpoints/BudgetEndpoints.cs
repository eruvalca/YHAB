using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using YHAB.Features.Budgeting.Services;
using YHAB.SharedKernel.Budgeting;

namespace YHAB.Features.Budgeting.Endpoints;

internal static class BudgetEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/plans").RequireAuthorization().WithTags("Budgeting");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-cache, no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                try
                {
                    await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return TypedResults.Problem(statusCode: 400, title: "Refresh this page and try again.", detail: "The request verification token is invalid.");
                }
            }

            try
            {
                return await next(context);
            }
            catch (BudgetRequestException exception)
            {
                return TypedResults.Problem(statusCode: exception.Status, title: "Unable to update plan", detail: exception.Message);
            }
        });
        group.MapGet("/", async (HttpContext context, BudgetStore store, CancellationToken cancellationToken)
            => TypedResults.Ok(await store.ListAsync(Owner(context), cancellationToken))).WithSummary("List plans you own or can edit.");
        group.MapGet("/token", (HttpContext context, IAntiforgery antiforgery)
            => TypedResults.Ok(new RequestToken(antiforgery.GetAndStoreTokens(context).RequestToken!))).WithSummary("Get a token for authenticated plan changes.");
        group.MapGet("/{planId:guid}", ReadAsync).WithSummary("Read a plan you can open.");
        group.MapGet("/{planId:guid}/view", async (Guid planId, HttpContext context, BudgetQueries queries, CancellationToken token)
            => TypedResults.Ok(await queries.ViewAsync(Owner(context), planId, token)));
        group.MapGet("/{planId:guid}/workspace", async (Guid planId, DateOnly? month, HttpContext context, BudgetQueries queries, CancellationToken token)
            => TypedResults.Ok(await queries.WorkspaceAsync(Owner(context), planId, month, token)))
            .WithSummary("Read matching navigation balances and one budget month.");
        group.MapGet("/{planId:guid}/revision", async (Guid planId, HttpContext context, BudgetQueries queries, CancellationToken token)
            => TypedResults.Ok(await queries.RevisionAsync(Owner(context), planId, token)));
        group.MapGet("/{planId:guid}/months/{month}", async (Guid planId, DateOnly month, long version, HttpContext context, BudgetQueries queries, CancellationToken token)
            => TypedResults.Ok(await queries.MonthAsync(Owner(context), planId, month, version, token)));
        group.MapGet("/{planId:guid}/register", async (Guid planId, string query, HttpContext context, BudgetQueries queries, CancellationToken token) =>
        {
            RegisterQuery request;
            try { request = JsonSerializer.Deserialize<RegisterQuery>(query, JsonSerializerOptions.Web) ?? throw new JsonException(); }
            catch (JsonException) { throw new BudgetRequestException(400, "Choose valid register filters."); }
            return TypedResults.Ok(await queries.RegisterAsync(Owner(context), planId, request, token));
        });
        group.MapGet("/{planId:guid}/reports", async (Guid planId, DateOnly from, DateOnly through, HttpContext context, BudgetQueries queries, CancellationToken token)
            => TypedResults.Ok(await queries.ReportsAsync(Owner(context), planId, from, through, token)));
        group.MapGet("/{planId:guid}/payees", async (Guid planId, HttpContext context, BudgetQueries queries, CancellationToken token)
            => TypedResults.Ok(await queries.PayeesAsync(Owner(context), planId, token)));
        group.MapGet("/{planId:guid}/sharing", async (Guid planId, HttpContext context, [FromServices] PlanSharingService sharing, CancellationToken token)
            => TypedResults.Ok(await sharing.ReadAsync(Owner(context), planId, token)))
            .WithSummary("Read who can open a plan.");
        group.MapPost("/{planId:guid}/sharing/codes", async (Guid planId, HttpContext context, [FromServices] PlanSharingService sharing, CancellationToken token)
            => TypedResults.Ok(await sharing.CreateShareCodeAsync(Owner(context), planId, token)))
            .WithSummary("Create a one-time code for sharing a plan.");
        group.MapPost("/join", async ([FromBody] JoinPlanRequest request, HttpContext context, [FromServices] PlanSharingService sharing, CancellationToken token)
            => TypedResults.Ok(await sharing.JoinAsync(Owner(context), request.Code, token)))
            .WithSummary("Join a plan with a one-time share code.");
        group.MapDelete("/{planId:guid}/sharing/{memberId}", async (Guid planId, string memberId, HttpContext context, [FromServices] PlanSharingService sharing, CancellationToken token)
            => TypedResults.Ok(await sharing.UnshareAsync(Owner(context), planId, memberId, token)))
            .WithSummary("Stop sharing a plan with an account.");
        group.MapPost("/{planId:guid}/leave", async (Guid planId, HttpContext context, [FromServices] PlanSharingService sharing, CancellationToken token) =>
        {
            await sharing.LeaveAsync(Owner(context), planId, token);
            return TypedResults.NoContent();
        }).WithSummary("Leave a plan shared with you.");
        group.MapPost("/", async (CreatePlanRequest request, HttpContext context, BudgetStore store, CancellationToken cancellationToken) =>
        {
            var id = await store.CreateAsync(Owner(context), request, cancellationToken);
            return TypedResults.Created($"/api/plans/{id}", id);
        }).WithSummary("Create a USD plan.");
        MapResource<SaveAccount>(group, "accounts");
        MapResource<SaveGroup>(group, "groups");
        MapResource<SaveCategory>(group, "categories");
        group.MapPost("/{planId:guid}/groups/reorder", MutateAsync<ReorderGroup>);
        group.MapPost("/{planId:guid}/categories/reorder", MutateAsync<ReorderCategory>);
        MapResource<SaveTransaction>(group, "transactions");
        group.MapPost("/{planId:guid}/transactions/delete", MutateAsync<DeleteTransactions>)
            .WithSummary("Delete Transactions in a plan you can edit.");
        group.MapPut("/{planId:guid}/assignments", MutateAsync<AssignMoney>)
            .WithSummary("Assign Money in a plan you can edit.");
        group.MapPost("/{planId:guid}/money-moves", MutateAsync<MoveMoney>)
            .WithSummary("Move Money in a plan you can edit.");
        group.MapPost("/{planId:guid}/auto-assign", MutateAsync<AutoAssign>)
            .WithSummary("Auto Assign in a plan you can edit.");
        group.MapPut("/{planId:guid}/settings", MutateAsync<UpdatePlan>)
            .WithSummary("Update Plan in a plan you can edit.");
        group.MapPost("/{planId:guid}/payees/rename", MutateAsync<RenamePayee>)
            .WithSummary("Rename Payee in a plan you can edit.");
        group.MapPost("/{planId:guid}/categories/merge", MutateAsync<RemoveCategory>)
            .WithSummary("Remove Category in a plan you can edit.");
        group.MapPost("/{planId:guid}/undo", MutateAsync<UndoChange>)
            .WithSummary("Undo Change in a plan you can edit.");
        group.MapPost("/{planId:guid}/redo", MutateAsync<RedoChange>)
            .WithSummary("Redo Change in a plan you can edit.");
        group.MapPost("/{planId:guid}/recurring/post-due", MutateAsync<PostRecurring>)
            .WithSummary("Post Recurring in a plan you can edit.");
    }

    private static void MapResource<TCommand>(RouteGroupBuilder group, string collection) where TCommand : PlanCommand
    {
        group.MapPost($"/{{planId:guid}}/{collection}", CreateResourceAsync<TCommand>).WithSummary($"Create an item in {collection}.");
        group.MapPut($"/{{planId:guid}}/{collection}/{{resourceId:guid}}", UpdateResourceAsync<TCommand>).WithSummary($"Update an item in {collection}.");
    }

    private static Task<Results<Ok<RevisionRequest>, ProblemHttpResult>> CreateResourceAsync<TCommand>(
        Guid planId, [FromBody] TCommand request, HttpContext context, BudgetStore store, CancellationToken cancellationToken) where TCommand : PlanCommand
        => ResourceId(request) != Guid.Empty
            ? Task.FromResult<Results<Ok<RevisionRequest>, ProblemHttpResult>>(TypedResults.Problem(statusCode: 400, title: "New resources must use an empty identifier."))
            : MutateAsync(planId, request, context, store, cancellationToken);

    private static Task<Results<Ok<RevisionRequest>, ProblemHttpResult>> UpdateResourceAsync<TCommand>(
        Guid planId, Guid resourceId, [FromBody] TCommand request, HttpContext context, BudgetStore store, CancellationToken cancellationToken) where TCommand : PlanCommand
        => ResourceId(request) != resourceId
            ? Task.FromResult<Results<Ok<RevisionRequest>, ProblemHttpResult>>(TypedResults.Problem(statusCode: 400, title: "The route and resource identifiers must match."))
            : MutateAsync(planId, request, context, store, cancellationToken);

    private static Guid? ResourceId(PlanCommand request) => request switch
    {
        SaveAccount item => item.Account?.Id,
        SaveGroup item => item.Group?.Id,
        SaveCategory item => item.Category?.Id,
        SaveTransaction item => item.Transaction?.Id,
        _ => null,
    };

    private static async Task<Results<Ok<PlanSnapshot>, ProblemHttpResult>> ReadAsync(Guid planId, HttpContext context,
        BudgetStore store, CancellationToken cancellationToken)
    {
        var plan = await store.ReadAsync(Owner(context), planId, cancellationToken);
        return plan is null ? TypedResults.Problem(statusCode: 404, title: "Plan not found") : TypedResults.Ok(plan);
    }

    private static async Task<Results<Ok<RevisionRequest>, ProblemHttpResult>> MutateAsync<TCommand>(
        Guid planId, [FromBody] TCommand request, HttpContext context, BudgetStore store, CancellationToken cancellationToken)
        where TCommand : PlanCommand
    {
        var outcome = await store.ExecuteAsync(Owner(context), planId, request, cancellationToken);
        return outcome.Match<Results<Ok<RevisionRequest>, ProblemHttpResult>>(
            plan => TypedResults.Ok(new RevisionRequest(plan.Version)),
            invalid => TypedResults.Problem(statusCode: 400, title: "Check your entry", detail: invalid.Message),
            _ => TypedResults.Problem(statusCode: 404, title: "Plan not found"),
            _ => TypedResults.Problem(statusCode: 409, title: "Plan changed", detail: "This plan changed in another tab. Refresh before trying again."));
    }

    private static string Owner(HttpContext context)
        => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new BudgetRequestException(401, "Please sign in again.");
}
