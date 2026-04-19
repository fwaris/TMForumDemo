namespace Tmf921.IntentManagement.Api.Controllers

open System
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

[<ApiController>]
[<Route(ApiRouteTemplates.IntentSpecificationCollection)>]
type IntentSpecificationController(shellStore: ShellStore) =
    inherit ControllerBase()

    let getTypeOrDefault (body: JsonObject) fallback =
        match body["@type"] with
        | null -> fallback
        | value -> value.GetValue<string>()

    let upsert (resourceHref: string) (id: string) (payload: JsonElement) =
        let body = ensureObject payload
        body["id"] <- JsonValue.Create(id)
        body["href"] <- JsonValue.Create(resourceHref)
        body["@type"] <- JsonValue.Create(getTypeOrDefault body "IntentSpecification")
        body["lastUpdate"] <- JsonValue.Create(nowText())
        shellStore.IntentSpecifications[id] <- body
        body

    [<HttpGet>]
    member this.List([<FromQuery>] fields: string, [<FromQuery>] offset: string, [<FromQuery>] limit: string) : IActionResult =
        shellStore.IntentSpecifications.Values |> Seq.toList |> selectFields fields |> this.Ok :> IActionResult

    [<HttpGet("{id}", Name = ApiRouteNames.IntentSpecificationGetById)>]
    member this.Get(id: string, [<FromQuery>] fields: string) : IActionResult =
        match shellStore.IntentSpecifications.TryGetValue id with
        | true, value -> this.Ok(value) :> IActionResult
        | _ -> this.NotFound() :> IActionResult

    [<HttpPost>]
    member this.Create([<FromBody>] payload: JsonElement, [<FromQuery>] fields: string) : IActionResult =
        let id = Guid.NewGuid().ToString("N")
        let routeValues = ApiLinks.routeValues [ "id", box id ]
        let resourceHref =
            ApiLinks.linkOrPath this ApiRouteNames.IntentSpecificationGetById routeValues (ApiRoutePaths.intentSpecificationItem id)
        let body = upsert resourceHref id payload
        this.CreatedAtRoute(ApiRouteNames.IntentSpecificationGetById, routeValues, selectFields fields body) :> IActionResult

    [<HttpPatch("{id}")>]
    member this.Patch(id: string, [<FromBody>] payload: JsonElement, [<FromQuery>] fields: string) : IActionResult =
        match shellStore.IntentSpecifications.TryGetValue id with
        | true, _ ->
            let routeValues = ApiLinks.routeValues [ "id", box id ]
            let resourceHref =
                ApiLinks.linkOrPath this ApiRouteNames.IntentSpecificationGetById routeValues (ApiRoutePaths.intentSpecificationItem id)
            upsert resourceHref id payload |> selectFields fields |> this.Ok :> IActionResult
        | _ -> this.NotFound() :> IActionResult

    [<HttpDelete("{id}")>]
    member _.Delete(id: string) : IActionResult =
        shellStore.IntentSpecifications.TryRemove(id) |> ignore
        base.NoContent() :> IActionResult
