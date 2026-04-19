namespace Tmf921.IntentManagement.Api.Controllers

open System
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

[<ApiController>]
[<Route(ApiRouteTemplates.IntentReportCollection)>]
type IntentReportController(shellStore: ShellStore) =
    inherit ControllerBase()

    let normalizeIntentId (intentId: string) (fallbackId: string) =
        if String.IsNullOrWhiteSpace(intentId) || String.Equals(intentId, "undefined", StringComparison.OrdinalIgnoreCase) then
            fallbackId
        else
            intentId

    let key (intentId: string) (id: string) = $"{intentId}:{id}"

    let sampleIntentReport (_intentId: string) (reportId: string) (reportHref: string) =
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
                let routeValues =
                    ApiLinks.routeValues
                        [ "intentId", box intentId
                          "id", box reportId ]

                let reportHref =
                    ApiLinks.linkOrPath
                        controller
                        ApiRouteNames.IntentReportGetById
                        routeValues
                        (ApiRoutePaths.intentReportItem intentId reportId)

                sampleIntentReport intentId reportId reportHref))

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

    [<HttpGet("{id}", Name = ApiRouteNames.IntentReportGetById)>]
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
