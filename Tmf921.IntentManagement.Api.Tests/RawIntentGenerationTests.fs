namespace Tmf921.IntentManagement.Api.Tests

open System
open System.IO
open Xunit
open Tmf921.IntentManagement.Api
open Tmf921.IntentManagement.Api.Tests.TestHelpers

type RawIntentGenerationTests() =
    [<Theory>]
    [<InlineData(429, "HTTP 429 (insufficient_quota: insufficient_quota)", "OPENAI_QUOTA_EXHAUSTED", "quota_exhausted", true)>]
    [<InlineData(429, "HTTP 429 (rate_limit_exceeded: rate_limit_exceeded)", "OPENAI_RATE_LIMITED", "rate_limited", false)>]
    [<InlineData(401, "HTTP 401 invalid_api_key", "OPENAI_AUTH_ERROR", "auth_error", true)>]
    [<InlineData(400, "HTTP 400 unsupported parameter", "OPENAI_BAD_REQUEST", "bad_request", true)>]
    [<InlineData(503, "HTTP 503 server_error", "OPENAI_UNAVAILABLE", "unavailable", false)>]
    member _.``OpenAI transport failures map to safe diagnostics``
        (status: int, text: string, expectedCode: string, expectedOutcome: string, expectedTerminal: bool)
        =
        let actual = RawIntentOpenAiErrors.classify (Some status) text

        Assert.Equal(expectedCode, actual.Code)
        Assert.Equal(expectedOutcome, actual.Outcome)
        Assert.Equal(expectedTerminal, actual.Terminal)
        Assert.DoesNotContain(" at ", actual.Details |> Option.defaultValue "")
        Assert.DoesNotContain("Exception", actual.Message)

    [<Fact>]
    member _.``Malformed F* output fails validation``() =
        let envelope =
            { Status = "parsed"
              ModuleText = Some "module Broken\nopen TmForumTr292CommonCore\nlet nope = 1"
              Issues = [] }

        let _, issues, _ = RawIntentGenerationValidation.validateEnvelope envelope

        Assert.NotEmpty issues

    [<Fact>]
    member _.``Missing common-core witnesses surface the first failed witness``() =
        let intent =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_fail_tm_01")
            |> fun scenario -> scenario.ReferenceIntent |> Option.get

        let moduleName = "WitnessTest_" + Guid.NewGuid().ToString("N")
        let candidate =
            IntentAdmission.buildCandidateModule moduleName intent
            |> IntentAdmission.tryParseCandidateModule
            |> function
                | Ok parsed -> parsed
                | Error issues -> failwithf "Expected test module to parse, but got %A" issues

        let outputDir = Path.Combine(Path.GetTempPath(), "tmf921-tests", Guid.NewGuid().ToString("N"))
        let checks = IntentAdmission.runAdmissionChecks outputDir candidate

        Assert.Equal<string option>(Some "measurable_intent", checks.FirstFailedWitness)

    [<Fact>]
    member _.``Reversed windows fail after common-core validation``() =
        let intent =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_fail_window_01")
            |> fun scenario -> scenario.ReferenceIntent |> Option.get

        let moduleName = "WindowWitnessTest_" + Guid.NewGuid().ToString("N")
        let candidate =
            IntentAdmission.buildCandidateModule moduleName intent
            |> IntentAdmission.tryParseCandidateModule
            |> function
                | Ok parsed -> parsed
                | Error issues -> failwithf "Expected test module to parse, but got %A" issues

        let outputDir = Path.Combine(Path.GetTempPath(), "tmf921-tests", Guid.NewGuid().ToString("N"))
        let checks = IntentAdmission.runAdmissionChecks outputDir candidate

        Assert.Equal("passed", checks.TmWitnessStatus)
        Assert.Equal("failed", checks.ProviderWitnessStatus)
        Assert.Equal("tm_validated_only", checks.AdmissionOutcome)
        Assert.Equal<string option>(Some "window_checked_intent", checks.FirstFailedWitness)

    [<Fact>]
    member _.``Policy failures remain provider-side after common-core validation``() =
        let intent =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "critical_fail_policy_01")
            |> fun scenario -> scenario.ReferenceIntent |> Option.get

        let moduleName = "PolicyWitnessTest_" + Guid.NewGuid().ToString("N")
        let candidate =
            IntentAdmission.buildCandidateModule moduleName intent
            |> IntentAdmission.tryParseCandidateModule
            |> function
                | Ok parsed -> parsed
                | Error issues -> failwithf "Expected test module to parse, but got %A" issues

        let outputDir = Path.Combine(Path.GetTempPath(), "tmf921-tests", Guid.NewGuid().ToString("N"))
        let checks = IntentAdmission.runAdmissionChecks outputDir candidate

        Assert.Equal("passed", checks.TmWitnessStatus)
        Assert.Equal("failed", checks.ProviderWitnessStatus)
        Assert.Equal("tm_validated_only", checks.AdmissionOutcome)
        Assert.Equal<string option>(Some "policy_checked_intent", checks.FirstFailedWitness)

    [<Fact>]
    member _.``Repair simulation succeeds on the second attempt``() =
        let brokenEnvelope =
            { Status = "parsed"
              ModuleText = Some "module Broken\nopen TmForumTr292CommonCore\nlet nope = 1"
              Issues = [] }

        let validEnvelope =
            let intent =
                DemoScenarios.scenarios
                |> List.find (fun scenario -> scenario.Id = "broadcast_success_01")
                |> fun scenario -> scenario.ReferenceIntent |> Option.get

            parsedResultFromIntent intent
            |> fun result -> result.Envelope |> Option.get

        let simulated =
            RawIntentGenerationValidation.simulateAttemptSequence [ brokenEnvelope; validEnvelope ]

        Assert.Equal("parsed", simulated.SelectedOutcome)
        Assert.Equal(2, simulated.Attempts.Length)
        Assert.True(simulated.Envelope.IsSome)
        Assert.Empty simulated.Diagnostics
