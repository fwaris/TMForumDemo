namespace Tmf921.IntentManagement.Api.Controllers

open System
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

[<ApiController>]
[<Route(ApiRouteTemplates.HubCollection)>]
type HubController(shellStore: ShellStore) =
    inherit ControllerBase()

    let getTypeOrDefault (body: JsonObject) fallback =
        match body["@type"] with
        | null -> fallback
        | value -> value.GetValue<string>()

    [<HttpPost>]
    member this.Create([<FromBody>] payload: JsonElement) : IActionResult =
        let id = Guid.NewGuid().ToString("N")
        let resourceHref = ApiLinks.fromCurrentCollection this id
        let body = ensureObject payload
        body["id"] <- JsonValue.Create(id)
        body["href"] <- JsonValue.Create(resourceHref)
        body["@type"] <- JsonValue.Create(getTypeOrDefault body "Hub")
        shellStore.Hubs[id] <- body
        this.Response.Headers.Location <- resourceHref
        this.StatusCode(201, body) :> IActionResult

    [<HttpDelete("{id}")>]
    member _.Delete(id: string) : IActionResult =
        shellStore.Hubs.TryRemove(id) |> ignore
        base.NoContent() :> IActionResult
