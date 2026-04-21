namespace Tmf921.IntentManagement.Api.Tests

open System
open System.IO
open Xunit
open Tmf921.IntentManagement.Api
open Tmf921.IntentManagement.Api.Tests.TestHelpers

type RawIntentGenerationTests() =
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
