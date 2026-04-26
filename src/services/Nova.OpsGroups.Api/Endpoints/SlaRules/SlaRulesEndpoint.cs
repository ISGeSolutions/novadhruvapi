namespace Nova.OpsGroups.Api.Endpoints.SlaRules;

// Old grouptour_sla_rules endpoints — table dropped in V001 rewrite.
// Routes kept as 410 Gone so existing clients get a clear signal.
public static class SlaRulesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/grouptour-task-sla-rules",
                      () => TypedResults.Problem(
                          title:      "Gone",
                          detail:     "Replaced by POST /api/v1/group-task-sla-hierarchy",
                          statusCode: StatusCodes.Status410Gone))
             .WithName("RemovedSlaRulesFetch");

        group.MapPatch("/grouptour-task-sla-rules",
                       () => TypedResults.Problem(
                           title:      "Gone",
                           detail:     "Replaced by PATCH /api/v1/group-task-sla-rule-save",
                           statusCode: StatusCodes.Status410Gone))
             .WithName("RemovedSlaRulesSave");
    }
}
