namespace Tmf921.IntentManagement.Api.Tests

open System.Linq
open Microsoft.AspNetCore.Mvc
open Xunit
open Tmf921.IntentManagement.Api
open Tmf921.IntentManagement.Api.Tests.TestHelpers

type IntentCreatePathTests() =
    [<Fact>]
    member _.``POST intent admits accepted scenarios``() =
        let generator = generatorFromScenarioText ()
        let store, shellStore, controller = createIntentController generator
        let payload =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_success_01")
            |> fun scenario -> createIntentPayload scenario.Text

        let result = controller.Create(payload, null).GetAwaiter().GetResult()
        let created = Assert.IsType<CreatedAtRouteResult>(result)
        Assert.NotNull created

        let resource = store.List() |> List.exactlyOne
        let processing = shellStore.ProcessingRecords[resource.Id]

        Assert.Equal(Some "provider_admitted", processing.AdmissionOutcome)
        Assert.Equal(ProcessingStatus.Checked, processing.Status)

    [<Fact>]
    member _.``POST intent rejects TM-invalid scenarios``() =
        let generator = generatorFromScenarioText ()
        let store, shellStore, controller = createIntentController generator
        let payload =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_fail_tm_01")
            |> fun scenario -> createIntentPayload scenario.Text

        controller.Create(payload, null).GetAwaiter().GetResult() |> ignore

        let resource = store.List() |> List.exactlyOne
        let processing = shellStore.ProcessingRecords[resource.Id]

        Assert.Equal(Some "measurable_intent", processing.FirstFailedWitness)
        Assert.Equal(ProcessingStatus.Rejected, processing.Status)

    [<Fact>]
    member _.``POST intent rejects provider-invalid scenarios``() =
        let generator = generatorFromScenarioText ()
        let store, shellStore, controller = createIntentController generator
        let payload =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "critical_fail_capacity_01")
            |> fun scenario -> createIntentPayload scenario.Text

        controller.Create(payload, null).GetAwaiter().GetResult() |> ignore

        let resource = store.List() |> List.exactlyOne
        let processing = shellStore.ProcessingRecords[resource.Id]

        Assert.Equal(Some "capacity_checked_intent", processing.FirstFailedWitness)
        Assert.Equal(ProcessingStatus.Rejected, processing.Status)

    [<Fact>]
    member _.``POST intent treats reversed windows as provider-side failures``() =
        let generator = generatorFromScenarioText ()
        let store, shellStore, controller = createIntentController generator
        let payload =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_fail_window_01")
            |> fun scenario -> createIntentPayload scenario.Text

        controller.Create(payload, null).GetAwaiter().GetResult() |> ignore

        let resource = store.List() |> List.exactlyOne
        let processing = shellStore.ProcessingRecords[resource.Id]

        Assert.Equal(Some "window_checked_intent", processing.FirstFailedWitness)
        Assert.Equal(Some "tm_validated_only", processing.AdmissionOutcome)
        Assert.Equal(Some "passed", processing.TmWitnessStatus)
        Assert.Equal(Some "failed", processing.ProviderWitnessStatus)
        Assert.Equal(ProcessingStatus.Rejected, processing.Status)

    [<Fact>]
    member _.``POST intent retries after downstream admission failure``() =
        let invalidIntent =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_fail_tm_01")
            |> fun scenario -> scenario.ReferenceIntent |> Option.get

        let validIntent =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_success_01")
            |> fun scenario -> scenario.ReferenceIntent |> Option.get

        let mutable callCount = 0
        let contexts = ResizeArray<RawIntentGenerationContext>()

        let generator =
            DelegateRawIntentGenerator(fun context _ ->
                callCount <- callCount + 1
                contexts.Add context

                match callCount with
                | 1 -> parsedLiveResultFromIntent invalidIntent
                | 2 -> parsedLiveResultFromIntent validIntent
                | _ -> failwith "Unexpected additional checker retry")
            :> IRawIntentGenerator

        let store, shellStore, controller = createIntentController generator
        let payload =
            DemoScenarios.scenarios
            |> List.find (fun scenario -> scenario.Id = "broadcast_success_01")
            |> fun scenario -> createIntentPayload scenario.Text

        controller.Create(payload, null).GetAwaiter().GetResult() |> ignore

        let resource = store.List() |> List.exactlyOne
        let processing = shellStore.ProcessingRecords[resource.Id]

        Assert.Equal(2, callCount)
        Assert.Equal(2, contexts.Count)
        Assert.Empty contexts[0].RepairIssues
        Assert.NotEmpty contexts[1].RepairIssues
        Assert.Contains(contexts[1].RepairIssues, fun issue -> issue.Code = "DOWNSTREAM_ADMISSION_FAILURE")
        Assert.Equal(Some "provider_admitted", processing.AdmissionOutcome)
        Assert.Equal(ProcessingStatus.Checked, processing.Status)
        Assert.Equal(2, processing.LlmParse |> Option.map (fun metadata -> metadata.Attempts.Length) |> Option.defaultValue 0)
