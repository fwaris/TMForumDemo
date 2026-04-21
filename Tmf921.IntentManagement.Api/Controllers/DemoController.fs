namespace Tmf921.IntentManagement.Api.Controllers

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Tmf921.IntentManagement.Api

type DemoValidateRequest =
    { scenarioId: string option
      text: string option }

[<ApiController>]
[<Route(ApiRouteTemplates.DemoCollection)>]
type DemoController(rawIntentGenerator: IRawIntentGenerator) =
    inherit ControllerBase()

    let scenarioSummary (scenario: DemoScenarios.DemoScenarioDefinition) =
        {| id = scenario.Id
           scenarioFamily = IntentAdmission.familyName scenario.ScenarioFamily
           title = scenario.Title
           kicker = scenario.Kicker
           expectedOutcome = string scenario.ExpectedOutcome
           expectedMessage = scenario.ExpectedMessage
           expectedJsonAccepted = scenario.ExpectedJsonAccepted
           expectedFailedWitness = scenario.ExpectedFailedWitness
           story = scenario.Story
           text = scenario.Text
           fStarFile = scenario.FStarFile |}

    let sameScenarioText (scenario: DemoScenarios.DemoScenarioDefinition) (text: string) =
        String.Equals(text.Trim(), scenario.Text.Trim(), StringComparison.Ordinal)

    let scenarioResponse
        (scenario: DemoScenarios.DemoScenarioDefinition)
        (referenceFStar: string option) =
        {| id = scenario.Id
           scenarioFamily = IntentAdmission.familyName scenario.ScenarioFamily
           title = scenario.Title
           kicker = scenario.Kicker
           expectedOutcome = string scenario.ExpectedOutcome
           expectedMessage = scenario.ExpectedMessage
           expectedJsonAccepted = scenario.ExpectedJsonAccepted
           expectedFailedWitness = scenario.ExpectedFailedWitness
           story = scenario.Story
           referenceFStar = referenceFStar |}

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
    member this.RunAll() : Task<IActionResult> =
        task {
            let! results = DemoScenarios.runAllAsync rawIntentGenerator
            return results |> this.Ok :> IActionResult
        }

    [<HttpGet("scenarios/{id}")>]
    member this.RunScenario(id: string) : Task<IActionResult> =
        task {
            match DemoScenarios.tryFindScenario id with
            | Some scenario ->
                let! result = DemoScenarios.runScenarioAsync rawIntentGenerator scenario
                return result |> this.Ok :> IActionResult
            | None ->
                return this.NotFound({| code = "NOT_FOUND"; message = $"Scenario '{id}' was not found." |}) :> IActionResult
        }

    [<HttpPost("validate")>]
    member this.Validate([<FromBody>] request: DemoValidateRequest) : Task<IActionResult> =
        task {
            let text = request.text |> Option.defaultValue "" |> fun value -> value.Trim()

            if String.IsNullOrWhiteSpace text then
                return this.BadRequest({| code = "VALIDATION_TEXT_REQUIRED"; message = "Validation text is required." |}) :> IActionResult
            else
                let selectedScenario =
                    request.scenarioId
                    |> Option.bind DemoScenarios.tryFindScenario
                    |> Option.filter (fun scenario -> sameScenarioText scenario text)

                let! validation, pipeline, scenario =
                    DemoScenarios.validateWithLivePipelineAsync rawIntentGenerator selectedScenario text

                let referenceFStar = scenario.FStarFile |> Option.bind DemoScenarios.tryReadFStarSource

                return
                    {| scenario = scenarioResponse scenario referenceFStar
                       validation = validation
                       pipeline = pipeline |}
                    |> this.Ok :> IActionResult
        }

    [<HttpPost("scenarios/refresh-fixtures")>]
    member this.RefreshFixtures() : Task<IActionResult> =
        task {
            let updates = ResizeArray<obj>()

            for scenario in DemoScenarios.scenarios do
                let! generated : RawIntentGenerationResult =
                    rawIntentGenerator.GenerateIntentModuleAsync(
                        { ScenarioId = Some scenario.Id
                          UseScenarioFixtures = false
                          RepairIssues = [] },
                        scenario.Text,
                        this.HttpContext.RequestAborted
                    )

                match generated.Envelope with
                | Some envelope ->
                    let fixture : ScenarioRawIntentFixture =
                        { ScenarioId = scenario.Id
                          Model = generated.Metadata.Model |> Option.defaultValue IntentLlmDefaults.value.Model
                          PromptVersion = generated.Metadata.PromptVersion |> Option.defaultValue ""
                          PromptText = generated.PromptText |> Option.defaultValue ""
                          ResponseText =
                            generated.RawResponseText
                            |> Option.defaultValue (JsonSerializer.Serialize(envelope, serializerOptions))
                          Envelope = envelope }

                    let path = RawIntentScenarioFixtures.write fixture

                    updates.Add(
                        {| id = scenario.Id
                           status = "written"
                           outcome = envelope.Status
                           path = path |}
                    )
                | None ->
                    updates.Add(
                        {| id = scenario.Id
                           status = "failed"
                           diagnostics = generated.Diagnostics |}
                    )

            return
                {| updated = updates.Count
                   results = updates |}
                |> this.Ok :> IActionResult
        }
