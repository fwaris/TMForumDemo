namespace Tmf921.IntentManagement.Api.Controllers

open System
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

[<ApiController>]
[<Route("tmf-api/intentManagement/v5/intent/{intentId}/intentReport")>]
type IntentReportController(shellStore: ShellStore, intentStore: IIntentStore) =
    inherit ControllerBase()

    let normalizeIntentId (intentId: string) (fallbackId: string) =
        if String.IsNullOrWhiteSpace(intentId) || String.Equals(intentId, "undefined", StringComparison.OrdinalIgnoreCase) then
            fallbackId
        else
            intentId

    let key (intentId: string) (id: string) = $"{intentId}:{id}"

    let basePath (c: ControllerBase) intentId =
        $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}/tmf-api/intentManagement/v5/intent/{intentId}/intentReport"

    let sampleIntentReport (_intentId: string) (reportId: string) (reportHref: string) (_intentHref: string) =
        let report = JsonObject()
        let expression = JsonObject()

        expression["@type"] <- JsonValue.Create("IntentExpression")
        expression["iri"] <- JsonValue.Create("sample_iri")
        expression["expressionValue"] <- JsonValue.Create("sample_expressionValue")

        report["@type"] <- JsonValue.Create("Intent")
        report["id"] <- JsonValue.Create(reportId)
        report["href"] <- JsonValue.Create(reportHref)
        report["name"] <- JsonValue.Create("sample_name")
        report["creationDate"] <- JsonValue.Create(nowText ())
        report["expression"] <- expression
        report

    let ensureShellReport (controller: ControllerBase) (intentId: string) (reportId: string) =
        shellStore.IntentReports.GetOrAdd(
            key intentId reportId,
            Func<string, JsonObject>(fun _ ->
                let reportHref = href (basePath controller intentId) reportId
                let intentHref = $"{controller.Request.Scheme}://{controller.Request.Host}{controller.Request.PathBase}/tmf-api/intentManagement/v5/intent/{intentId}"
                sampleIntentReport intentId reportId reportHref intentHref))

    [<HttpGet>]
    member this.List(intentId: string, [<FromQuery>] fields: string, [<FromQuery>] offset: string, [<FromQuery>] limit: string) : IActionResult =
        let intentId = normalizeIntentId intentId intentId
        let prefix = $"{intentId}:"
        ensureShellReport this intentId intentId |> ignore
        shellStore.IntentReports
        |> Seq.filter (fun pair -> pair.Key.StartsWith(prefix))
        |> Seq.map (fun pair -> pair.Value)
        |> Seq.toList
        |> selectFields fields
        |> this.Ok :> IActionResult

    [<HttpGet("{id}")>]
    member this.Get(intentId: string, id: string, [<FromQuery>] fields: string) : IActionResult =
        let intentId = normalizeIntentId intentId id
        match shellStore.IntentReports.TryGetValue(key intentId id) with
        | true, value -> value |> this.Ok :> IActionResult
        | _ ->
            let report = ensureShellReport this intentId id
            report |> this.Ok :> IActionResult

    [<HttpDelete("{id}")>]
    member _.Delete(intentId: string, id: string) : IActionResult =
        let intentId = normalizeIntentId intentId id
        shellStore.IntentReports.TryRemove(key intentId id) |> ignore
        base.NoContent() :> IActionResult
