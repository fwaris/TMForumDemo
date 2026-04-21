namespace Tmf921.IntentManagement.Api.Tests

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Mvc.Routing
open Microsoft.Extensions.Logging.Abstractions
open Tmf921.IntentManagement.Api
open Tmf921.IntentManagement.Api.Controllers

module TestHelpers =
    let private attempt outcome issues =
        { Attempt = 1
          Source = "test"
          Outcome = outcome
          ResponseId = None
          FinishReason = None
          Issues = issues }

    let private metadata usedFixture outcome issues =
        { Provider = Some "test"
          Model = Some "fixture-model"
          PromptVersion = Some "test-prompt"
          SelectedOutcome = Some outcome
          UsedFixture = usedFixture
          FixtureId = if usedFixture then Some "test-fixture" else None
          Attempts = [ attempt outcome issues ] }

    let parsedResultFromIntentWithFixtureFlag usedFixture (intent: IntentAdmission.RestrictedIntent) =
        let moduleName = "TestIntent_" + Guid.NewGuid().ToString("N")
        let moduleText = IntentAdmission.buildCandidateModule moduleName intent
        let envelope =
            { Status = "parsed"
              ModuleText = Some moduleText
              Issues = [] }

        { Envelope = Some envelope
          Metadata = metadata usedFixture "parsed" []
          PromptText = Some "test prompt"
          RawResponseText = envelope.ModuleText
          Diagnostics = [] }

    let parsedResultFromIntent (intent: IntentAdmission.RestrictedIntent) =
        parsedResultFromIntentWithFixtureFlag true intent

    let parsedLiveResultFromIntent (intent: IntentAdmission.RestrictedIntent) =
        parsedResultFromIntentWithFixtureFlag false intent

    let clarificationResult code message =
        let issues =
            [ { Code = code
                Message = message
                Details = None } ]

        let envelope =
            { Status = "clarification_required"
              ModuleText = None
              Issues = issues }

        { Envelope = Some envelope
          Metadata = metadata true "clarification_required" issues
          PromptText = Some "test prompt"
          RawResponseText = None
          Diagnostics = issues }

    type DelegateRawIntentGenerator(handler: RawIntentGenerationContext -> string -> RawIntentGenerationResult) =
        interface IRawIntentGenerator with
            member _.GenerateIntentModuleAsync(context, text, _) =
                Task.FromResult(handler context text)

    let generatorFromScenarioText () =
        let mapping =
            DemoScenarios.scenarios
            |> List.choose (fun scenario ->
                scenario.ReferenceIntent
                |> Option.map (fun intent -> scenario.Text, intent))
            |> Map.ofList

        DelegateRawIntentGenerator(fun _ text ->
            match mapping |> Map.tryFind text with
            | Some intent -> parsedResultFromIntent intent
            | None -> clarificationResult "NO_MAPPING" "No fixture mapping exists for this test prompt.")
        :> IRawIntentGenerator

    let createIntentPayload (text: string) =
        let request = DemoScenarios.buildNaturalLanguageRequest text
        JsonSerializer.SerializeToElement(request, serializerOptions)

    let createIntentController (generator: IRawIntentGenerator) =
        let store = IntentStore() :> IIntentStore
        let shellStore = ShellStore()
        let logger = NullLogger<IntentController>.Instance
        let controller = IntentController(store, shellStore, generator, logger)
        let httpContext = DefaultHttpContext()
        httpContext.Request.Scheme <- "https"
        httpContext.Request.Host <- HostString("example.test")
        httpContext.Request.Path <- PathString("/tmf-api/intentManagement/v5/intent")
        controller.ControllerContext <- ControllerContext()
        controller.ControllerContext.HttpContext <- httpContext
        controller.Url <-
            { new IUrlHelper with
                member _.ActionContext = controller.ControllerContext
                member _.Action(_) = null
                member _.Content(contentPath) = contentPath
                member _.IsLocalUrl(_) = false
                member _.Link(_, _) = null
                member _.RouteUrl(_) = null }
        store, shellStore, controller
