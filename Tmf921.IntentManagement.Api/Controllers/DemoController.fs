namespace Tmf921.IntentManagement.Api.Controllers

open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

[<ApiController>]
[<Route("tmf-api/intentManagement/v5/demo")>]
type DemoController() =
    inherit ControllerBase()

    [<HttpGet("scenarios")>]
    member this.ListScenarios() : IActionResult =
        DemoScenarios.scenarios
        |> List.map (fun scenario ->
            {| id = scenario.Id
               expectedOutcome = string scenario.ExpectedOutcome
               expectedMessage = scenario.ExpectedMessage
               text = scenario.Text |})
        |> this.Ok :> IActionResult

    [<HttpGet("scenarios/run")>]
    member this.RunAll() : IActionResult =
        DemoScenarios.runAll() |> this.Ok :> IActionResult

    [<HttpGet("scenarios/{id}")>]
    member this.RunScenario(id: string) : IActionResult =
        match DemoScenarios.scenarios |> List.tryFind (fun scenario -> scenario.Id = id) with
        | Some scenario -> DemoScenarios.runScenario scenario |> this.Ok :> IActionResult
        | None -> this.NotFound({| code = "NOT_FOUND"; message = $"Scenario '{id}' was not found." |}) :> IActionResult
