namespace Tmf921.IntentManagement.Api.Controllers

open System
open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

type DemoValidateRequest =
    { scenarioId: string option
      text: string option }

[<ApiController>]
[<Route(ApiRouteTemplates.DemoCollection)>]
type DemoController() =
    inherit ControllerBase()

    let scenarioSummary (scenario: DemoScenarios.DemoScenarioDefinition) =
        {| id = scenario.Id
           expectedOutcome = string scenario.ExpectedOutcome
           expectedMessage = scenario.ExpectedMessage
           text = scenario.Text
           fStarFile = scenario.FStarFile |}

    [<HttpGet("scenarios")>]
    member this.ListScenarios() : IActionResult =
        DemoScenarios.scenarios
        |> List.map scenarioSummary
        |> this.Ok :> IActionResult

    [<HttpGet("featured-scenarios")>]
    member this.ListFeaturedScenarios() : IActionResult =
        DemoScenarios.featuredScenarios
        |> List.map scenarioSummary
        |> this.Ok :> IActionResult

    [<HttpGet("scenarios/run")>]
    member this.RunAll() : IActionResult =
        DemoScenarios.runAll() |> this.Ok :> IActionResult

    [<HttpGet("scenarios/{id}")>]
    member this.RunScenario(id: string) : IActionResult =
        match DemoScenarios.tryFindScenario id with
        | Some scenario -> DemoScenarios.runScenario scenario |> this.Ok :> IActionResult
        | None -> this.NotFound({| code = "NOT_FOUND"; message = $"Scenario '{id}' was not found." |}) :> IActionResult

    [<HttpPost("validate")>]
    member this.Validate([<FromBody>] request: DemoValidateRequest) : IActionResult =
        let text = request.text |> Option.defaultValue "" |> fun value -> value.Trim()

        if String.IsNullOrWhiteSpace text then
            this.BadRequest({| code = "VALIDATION_TEXT_REQUIRED"; message = "Validation text is required." |}) :> IActionResult
        else
            let scenario =
                request.scenarioId
                |> Option.bind DemoScenarios.tryFindScenario
                |> Option.defaultWith (fun () ->
                    { Id = "ad_hoc"
                      Text = text
                      ExpectedOutcome = DemoScenarios.DemoAccept
                      ExpectedMessage = "Ad hoc validation request."
                      FStarFile = None
                      FStarExpectedSuccess = None })

            let validation = DemoScenarios.validateText text
            let pipeline =
                let intentId = $"demo-{Guid.NewGuid():N}"
                let outcome = IntentPipeline.processIntent intentId (DemoScenarios.buildNaturalLanguageRequest text)
                outcome.ProcessingRecord

            let referenceFStar =
                scenario.FStarFile |> Option.bind DemoScenarios.tryReadFStarSource

            {| scenario =
                 {| id = scenario.Id
                    expectedOutcome = string scenario.ExpectedOutcome
                    expectedMessage = scenario.ExpectedMessage
                    referenceFStar = referenceFStar |}
               validation = validation
               pipeline = pipeline |}
            |> this.Ok :> IActionResult
