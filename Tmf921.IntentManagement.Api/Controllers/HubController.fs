namespace Tmf921.IntentManagement.Api.Controllers

open System
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

[<ApiController>]
[<Route("tmf-api/intentManagement/v5/hub")>]
type HubController(shellStore: ShellStore) =
    inherit ControllerBase()

    let basePath (c: ControllerBase) =
        $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}/tmf-api/intentManagement/v5/hub"

    let getTypeOrDefault (body: JsonObject) fallback =
        match body["@type"] with
        | null -> fallback
        | value -> value.GetValue<string>()

    [<HttpPost>]
    member this.Create([<FromBody>] payload: JsonElement) : IActionResult =
        let id = Guid.NewGuid().ToString("N")
        let body = ensureObject payload
        body["id"] <- JsonValue.Create(id)
        body["href"] <- JsonValue.Create(href (basePath this) id)
        body["@type"] <- JsonValue.Create(getTypeOrDefault body "Hub")
        shellStore.Hubs[id] <- body
        this.Response.Headers.Location <- href (basePath this) id
        this.StatusCode(201, body) :> IActionResult

    [<HttpDelete("{id}")>]
    member _.Delete(id: string) : IActionResult =
        shellStore.Hubs.TryRemove(id) |> ignore
        base.NoContent() :> IActionResult
