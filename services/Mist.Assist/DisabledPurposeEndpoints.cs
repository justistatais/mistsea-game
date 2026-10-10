namespace Mist.Assist;

public static class DisabledPurposeEndpoints
{
    // Each purpose has its own route. Disabled mode does not parse, retain, or forward bodies.
    // Explicit offline test providers live in the test process, never in this server by default.
    public static void MapDisabledPurposeEndpoints(this WebApplication app)
    {
        foreach(string purpose in new[]{"explain","feedback","intent-candidate"})
            app.MapPost("/v1/"+purpose,()=>Results.Json(new{code="provider_disabled",assessment_status="unassessed",band=(double?)null,world_write_capability=false},statusCode:503));
    }
}
