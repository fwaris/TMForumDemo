namespace Tmf921.IntentManagement.Api.Tests

open System
open Xunit
open Tmf921.IntentManagement.Api

type BenchmarkRunnerTests() =
    [<Fact>]
    member _.``Default benchmark manifest contains 140 prompts``() =
        let manifest = BenchmarkRunner.defaultManifest ()

        Assert.Equal(140, manifest.PromptCount)
        Assert.Equal(140, manifest.Prompts.Length)

    [<Fact>]
    member _.``Default benchmark manifest emits 10 prompts per scenario``() =
        let manifest = BenchmarkRunner.defaultManifest ()

        let counts =
            manifest.Prompts
            |> List.countBy (fun prompt -> prompt.ScenarioId)

        Assert.All(
            counts,
            fun (_, count) ->
                Assert.Equal(10, count)
        )

    [<Fact>]
    member _.``Synthetic correctness manifest contains 10 accepted prompts``() =
        let manifest = BenchmarkRunner.defaultSyntheticCorrectnessManifest 30 10

        Assert.Equal(10, manifest.ExpressionCount)
        Assert.Equal(30, manifest.RepetitionCount)
        Assert.Equal(10, manifest.Prompts.Length)
        Assert.All(
            manifest.Prompts,
            fun prompt ->
                Assert.Equal("DemoAccept", prompt.ExpectedOutcome)
        )

    [<Fact>]
    member _.``Benchmark summary computes first-attempt and retry success rates``() =
        let results : BenchmarkRunner.BenchmarkPromptResult list =
            [ { PromptId = "a"
                ScenarioId = "s1"
                ScenarioFamily = "LiveBroadcast"
                PromptIndex = 1
                Text = "a"
                ExpectedOutcome = "DemoAccept"
                ExpectedFailedWitness = None
                Model = Some "fixture-model"
                PromptVersion = Some "test-prompt"
                AttemptCount = 1
                ParseSuccess = true
                TmTypeCheckSuccess = true
                ProviderAdmissionSuccess = true
                SelectedProfile = Some "LiveBroadcastGold"
                FirstFailedWitness = None
                AdmissionOutcome = Some "provider_admitted"
                RequestId = "1"
                ProcessingStatus = "Checked"
                ExpectationMatched = true
                FirstAttemptSuccess = true
                OneRetrySuccess = true }
              { PromptId = "b"
                ScenarioId = "s2"
                ScenarioFamily = "CriticalService"
                PromptIndex = 1
                Text = "b"
                ExpectedOutcome = "DemoRejectProvider"
                ExpectedFailedWitness = Some "capacity_checked_intent"
                Model = Some "fixture-model"
                PromptVersion = Some "test-prompt"
                AttemptCount = 2
                ParseSuccess = true
                TmTypeCheckSuccess = true
                ProviderAdmissionSuccess = false
                SelectedProfile = Some "CriticalCareAssured"
                FirstFailedWitness = Some "capacity_checked_intent"
                AdmissionOutcome = Some "tm_validated_only"
                RequestId = "2"
                ProcessingStatus = "Rejected"
                ExpectationMatched = true
                FirstAttemptSuccess = false
                OneRetrySuccess = true } ]

        let summary = BenchmarkRunner.summarizeResults (DateTimeOffset.Parse("2026-04-20T12:00:00Z")) results

        Assert.Equal(2, summary.PromptCount)
        Assert.Equal(3, summary.AttemptCount)
        Assert.Equal(0.5, summary.FirstAttemptSuccessRate, 5)
        Assert.Equal(1.0, summary.OneRetrySuccessRate, 5)
        Assert.Equal(1.0, summary.TwoRetrySuccessRate, 5)
        Assert.Contains(summary.FirstFailedWitnesses, fun item -> item.Witness = "capacity_checked_intent" && item.Count = 1)
