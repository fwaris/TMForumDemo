namespace Tmf921.IntentManagement.Api.Controllers

open System.Text.Json
open Microsoft.AspNetCore.Mvc

[<ApiController>]
[<Route("tmf-api/intentManagement/v5/listener")>]
type ListenerController() =
    inherit ControllerBase()

    [<HttpPost("intentAttributeValueChangeEvent")>]
    member this.IntentAttributeValueChangeEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentCreateEvent")>]
    member this.IntentCreateEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentDeleteEvent")>]
    member this.IntentDeleteEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentReportAttributeValueChangeEvent")>]
    member this.IntentReportAttributeValueChangeEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentReportCreateEvent")>]
    member this.IntentReportCreateEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentReportDeleteEvent")>]
    member this.IntentReportDeleteEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentSpecificationAttributeValueChangeEvent")>]
    member this.IntentSpecificationAttributeValueChangeEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentSpecificationCreateEvent")>]
    member this.IntentSpecificationCreateEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentSpecificationDeleteEvent")>]
    member this.IntentSpecificationDeleteEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentSpecificationStatusChangeEvent")>]
    member this.IntentSpecificationStatusChangeEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult

    [<HttpPost("intentStatusChangeEvent")>]
    member this.IntentStatusChangeEvent([<FromBody>] payload: JsonElement) : IActionResult = this.NoContent() :> IActionResult
