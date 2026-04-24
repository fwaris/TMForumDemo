namespace Tmf921.IntentManagement.Api

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks

module BenchmarkRunner =
    type BenchmarkPrompt =
        { PromptId: string
          ScenarioId: string
          ScenarioFamily: string
          PromptIndex: int
          Text: string
          ExpectedOutcome: string
          ExpectedFailedWitness: string option }

    type BenchmarkManifest =
        { ManifestVersion: string
          GeneratedAt: DateTimeOffset
          PromptCount: int
          Prompts: BenchmarkPrompt list }

    type BenchmarkWitnessCount =
        { Witness: string
          Count: int }

    type BenchmarkPromptResult =
        { PromptId: string
          ScenarioId: string
          ScenarioFamily: string
          PromptIndex: int
          Text: string
          ExpectedOutcome: string
          ExpectedFailedWitness: string option
          Model: string option
          PromptVersion: string option
          AttemptCount: int
          ParseSuccess: bool
          TmTypeCheckSuccess: bool
          ProviderAdmissionSuccess: bool
          SelectedProfile: string option
          FirstFailedWitness: string option
          AdmissionOutcome: string option
          RequestId: string
          ProcessingStatus: string
          ExpectationMatched: bool
          FirstAttemptSuccess: bool
          OneRetrySuccess: bool }

    type BenchmarkRunSummary =
        { Model: string option
          PromptVersion: string option
          Date: DateTimeOffset
          PromptCount: int
          AttemptCount: int
          ParseSuccessCount: int
          ParseSuccessRate: float
          TmTypeCheckSuccessCount: int
          TmTypeCheckSuccessRate: float
          ProviderAdmissionSuccessCount: int
          ProviderAdmissionSuccessRate: float
          FirstAttemptSuccessRate: float
          OneRetrySuccessRate: float
          TwoRetrySuccessRate: float
          FirstFailedWitnesses: BenchmarkWitnessCount list }

    type BenchmarkRunArtifact =
        { RunId: string
          Mode: string
          Manifest: BenchmarkManifest
          Results: BenchmarkPromptResult list
          Summary: BenchmarkRunSummary }

    type BenchmarkRateSummary =
        { SuccessCount: int
          TrialCount: int
          SuccessRate: float
          Wilson95Lower: float
          Wilson95Upper: float }

    type BenchmarkAttemptFrequency =
        { AttemptCount: int
          Count: int }

    type SyntheticCorrectnessManifest =
        { ManifestVersion: string
          SuiteId: string
          GeneratedAt: DateTimeOffset
          ExpressionCount: int
          RepetitionCount: int
          AttemptBudget: int
          Prompts: BenchmarkPrompt list }

    type SyntheticCorrectnessTrialResult =
        { TrialId: string
          Repetition: int
          Result: BenchmarkPromptResult
          SuccessWithinSecondRetry: bool
          LlmParse: LlmParseMetadata option
          Artifacts: SidecarArtifacts option
          Diagnostics: ProcessingDiagnostic list
          ProcessingRecordPath: string }

    type SyntheticCorrectnessSummary =
        { SuiteId: string
          ManifestVersion: string
          Model: string option
          PromptVersion: string option
          Date: DateTimeOffset
          ExpressionCount: int
          RepetitionCount: int
          TrialCount: int
          FirstAttempt: BenchmarkRateSummary
          FirstRetry: BenchmarkRateSummary
          SecondRetry: BenchmarkRateSummary
          SuccessfulTrials: int
          FailureTrials: int
          SuccessAttemptDistribution: BenchmarkAttemptFrequency list
          MaxAttemptCountObserved: int
          MeanAttemptsOnSuccessfulTrials: float
          MedianAttemptsOnSuccessfulTrials: float
          FirstFailedWitnesses: BenchmarkWitnessCount list }

    type SyntheticCorrectnessRunArtifact =
        { RunId: string
          Mode: string
          Manifest: SyntheticCorrectnessManifest
          Results: SyntheticCorrectnessTrialResult list
          Summary: SyntheticCorrectnessSummary }

    let private manifestVersion = "2026-04-20.benchmark.v1"
    let private syntheticCorrectnessManifestVersion = "2026-04-23.synthetic-correct.v1"
    let private syntheticCorrectnessSuiteId = "synthetic-correct-expressions"

    let private benchmarkRoot () =
        Path.Combine(repoRoot (), "artifacts", "benchmark-runs")

    let private syntheticCorrectnessRoot () =
        Path.Combine(benchmarkRoot (), "synthetic-correctness")

    let private jsonOptions () =
        let options = JsonSerializerOptions(serializerOptions)
        options.WriteIndented <- true
        options

    let private writeJson (path: string) value =
        let directory = Path.GetDirectoryName path

        if not (String.IsNullOrWhiteSpace directory) then
            Directory.CreateDirectory(directory) |> ignore

        File.WriteAllText(path, JsonSerializer.Serialize(value, jsonOptions ()), Encoding.UTF8)

    let private readJson<'T> path =
        JsonSerializer.Deserialize<'T>(File.ReadAllText(path, Encoding.UTF8), serializerOptions)

    let private formatDate (value: DateOnly) =
        value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)

    let private formatHour (value: int) =
        value.ToString("00", CultureInfo.InvariantCulture) + ":00"

    let private reportingPhrase minutes =
        match minutes with
        | 60 -> "hourly"
        | 5 -> "every 5 minutes"
        | 10 -> "every 10 minutes"
        | 15 -> "every 15 minutes"
        | value -> $"every {value} minutes"

    let private preservePolicySentence (intent: IntentAdmission.RestrictedIntent) =
        if intent.RequestPublicSafetyPreemption then
            "If needed, preempt reserved public-safety capacity to maintain service."
        else
            "Do not impact emergency-service traffic."

    let private broadcastPromptVariants (originalText: string) (intent: IntentAdmission.RestrictedIntent) =
        match intent.TargetName, intent.EventDate, intent.StartHour, intent.EndHour, intent.Timezone, intent.PrimaryDeviceCount, intent.MaxLatencyMs, intent.ReportingIntervalMinutes with
        | Some targetName, Some eventDate, Some startHour, Some endHour, Some timezone, Some deviceCount, Some latency, Some reporting ->
            let dateText = formatDate eventDate
            let startText = formatHour startHour
            let endText = formatHour endHour
            let reportingText = reportingPhrase reporting
            let policyText = preservePolicySentence intent

            [ originalText
              $"Reserve premium 5G broadcast connectivity for the live event at {targetName} on {dateText} from {startText} to {endText} {timezone}. Support up to {deviceCount} production devices, keep uplink latency below {latency} ms, send {reportingText} compliance updates, trigger immediate degradation alerts, and {policyText.ToLowerInvariant()}"
              $"For the broadcast at {targetName}, provide premium 5G service on {dateText} between {startText} and {endText} {timezone}. The service must handle {deviceCount} production devices, maintain uplink latency under {latency} ms, publish {reportingText} compliance updates, and raise immediate quality-degradation alerts. {policyText}"
              $"Need premium 5G broadcast service at {targetName} on {dateText}, {startText}-{endText} {timezone}. Admit {deviceCount} production devices, hold uplink latency below {latency} ms, produce {reportingText} compliance reporting, send immediate alerts on degradation, and {policyText.ToLowerInvariant()}"
              $"At {targetName}, deliver premium 5G broadcast service for the live event on {dateText} from {startText} to {endText} {timezone}. Support {deviceCount} production devices, keep uplink latency under {latency} ms, issue {reportingText} compliance updates, and alert immediately if service quality drops. {policyText}"
              $"Provide a premium 5G service for broadcasting at {targetName} on {dateText} from {startText} until {endText} {timezone}. Capacity must cover {deviceCount} production devices, uplink latency must stay below {latency} ms, compliance updates must be {reportingText}, degradation alerts must be immediate, and the policy must say: {policyText}"
              $"Please provision premium 5G broadcast coverage at {targetName} for {dateText}, {startText}-{endText} {timezone}. It needs {deviceCount} production-device capacity, uplink latency below {latency} ms, {reportingText} compliance reporting, immediate alerts for degradation, and this traffic policy: {policyText}"
              $"The live event at {targetName} needs premium 5G broadcast support on {dateText} from {startText} to {endText} {timezone}. Handle {deviceCount} production devices, hold uplink latency under {latency} ms, send {reportingText} compliance updates, raise immediate alerts when service quality degrades, and {policyText.ToLowerInvariant()}"
              $"Create a premium 5G broadcast intent for {targetName} on {dateText} between {startText} and {endText} {timezone}: {deviceCount} production devices, uplink latency below {latency} ms, {reportingText} compliance updates, immediate degradation alerts, and {policyText.ToLowerInvariant()}"
              $"Set up premium 5G live-broadcast service at {targetName} on {dateText}, from {startText} to {endText} {timezone}. The request covers {deviceCount} production devices, uplink latency under {latency} ms, {reportingText} compliance updates, immediate service-quality alerts, and {policyText.ToLowerInvariant()}" ]
        | _ ->
            [ originalText ]

    let private criticalPromptVariants (originalText: string) (intent: IntentAdmission.RestrictedIntent) =
        match intent.TargetName, intent.EventDate, intent.StartHour, intent.EndHour, intent.Timezone, intent.PrimaryDeviceCount, intent.AuxiliaryEndpointCount, intent.MaxLatencyMs, intent.ReportingIntervalMinutes with
        | Some targetName, Some eventDate, Some startHour, Some endHour, Some timezone, Some primaryCount, Some auxiliaryCount, Some latency, Some reporting ->
            let dateText = formatDate eventDate
            let startText = formatHour startHour
            let endText = formatHour endHour
            let reportingText = reportingPhrase reporting
            let policyText = preservePolicySentence intent

            [ originalText
              $"Provide ultra-reliable 5G clinical service for telemedicine and critical care at {targetName} on {dateText} from {startText} to {endText} {timezone}. Support up to {primaryCount} critical devices and {auxiliaryCount} auxiliary endpoints, keep end-to-end latency below {latency} ms, send compliance updates {reportingText}, trigger immediate degradation alerts, and {policyText.ToLowerInvariant()}"
              $"At {targetName}, reserve ultra-reliable 5G service for telemedicine and critical care on {dateText} between {startText} and {endText} {timezone}. The service must admit {primaryCount} critical devices plus {auxiliaryCount} auxiliary endpoints, hold end-to-end latency under {latency} ms, publish compliance updates {reportingText}, and alert immediately on degradation. {policyText}"
              $"Need a clinical 5G assurance service at {targetName} on {dateText}, {startText}-{endText} {timezone}. Support {primaryCount} critical devices and {auxiliaryCount} auxiliary endpoints, maintain end-to-end latency below {latency} ms, issue compliance updates {reportingText}, raise immediate alerts if service quality degrades, and {policyText.ToLowerInvariant()}"
              $"For telemedicine and critical-care operations at {targetName}, provide ultra-reliable 5G on {dateText} from {startText} to {endText} {timezone}. Capacity must cover {primaryCount} critical devices and {auxiliaryCount} auxiliary endpoints, end-to-end latency must stay below {latency} ms, compliance reporting must be {reportingText}, and alerts on degradation must be immediate. {policyText}"
              $"Provision an ultra-reliable clinical 5G service at {targetName} on {dateText} between {startText} and {endText} {timezone}. The request includes {primaryCount} critical devices, {auxiliaryCount} auxiliary endpoints, latency under {latency} ms end to end, {reportingText} compliance updates, immediate degradation alerts, and this policy: {policyText}"
              $"Please admit a telemedicine 5G service for {targetName} on {dateText}, {startText}-{endText} {timezone}. It needs {primaryCount} critical devices and {auxiliaryCount} auxiliary endpoints, end-to-end latency below {latency} ms, {reportingText} compliance reporting, immediate service-quality alerts, and {policyText.ToLowerInvariant()}"
              $"Create an ultra-reliable clinical-service intent for {targetName} on {dateText} from {startText} to {endText} {timezone}: {primaryCount} critical devices, {auxiliaryCount} auxiliary endpoints, end-to-end latency under {latency} ms, compliance updates {reportingText}, immediate degradation alerts, and {policyText.ToLowerInvariant()}"
              $"Set up an assured 5G service for telemedicine at {targetName} on {dateText}, from {startText} until {endText} {timezone}. Support {primaryCount} critical devices and {auxiliaryCount} auxiliary endpoints, keep end-to-end latency below {latency} ms, send compliance updates {reportingText}, alert immediately on degradation, and {policyText.ToLowerInvariant()}"
              $"The clinical operations team needs ultra-reliable 5G at {targetName} on {dateText} from {startText} to {endText} {timezone}. Admit {primaryCount} critical devices and {auxiliaryCount} auxiliary endpoints, keep end-to-end latency under {latency} ms, provide {reportingText} compliance updates, raise immediate alerts on degradation, and {policyText.ToLowerInvariant()}" ]
        | _ ->
            [ originalText ]

    let private vagueBroadcastPromptVariants =
        [ "Make the event network really good and fast for the broadcast."
          "The broadcast team needs better network performance for the event."
          "Please improve connectivity for the live broadcast at the venue."
          "We need the event broadcast network to be excellent and responsive."
          "Give the broadcast operation a strong event network."
          "Set up really good connectivity for the live event broadcast."
          "The production crew needs fast reliable networking for the broadcast."
          "Help the live event broadcast with better network service."
          "Provide strong network quality for the broadcast team."
          "Keep the event broadcast network in great shape." ]

    let private vagueCriticalPromptVariants =
        [ "Keep the telemedicine network ultra reliable for the hospital."
          "The hospital needs very reliable connectivity for telemedicine."
          "Please make the clinical network excellent for telemedicine."
          "We need strong dependable 5G for hospital telemedicine."
          "Improve the network for critical care communications."
          "Set up very reliable service for the hospital's telemedicine traffic."
          "Keep clinical operations on a really good network."
          "Give the hospital telemedicine team great connectivity."
          "Provide highly reliable service for critical clinical workflows."
          "Make sure telemedicine has excellent network performance at the hospital." ]

    let private promptVariantsForScenario (scenario: DemoScenarios.DemoScenarioDefinition) =
        match scenario.Id, scenario.ReferenceIntent with
        | "broadcast_fail_tm_01", _ -> vagueBroadcastPromptVariants
        | "critical_fail_tm_01", _ -> vagueCriticalPromptVariants
        | _, Some intent ->
            match intent.ScenarioFamily with
            | IntentAdmission.LiveBroadcast -> broadcastPromptVariants scenario.Text intent
            | IntentAdmission.CriticalService -> criticalPromptVariants scenario.Text intent
        | _, None -> [ scenario.Text ]

    let defaultManifest () =
        let prompts =
            DemoScenarios.scenarios
            |> List.collect (fun scenario ->
                promptVariantsForScenario scenario
                |> List.mapi (fun index text ->
                    { PromptId = sprintf "%s_p%02i" scenario.Id (index + 1)
                      ScenarioId = scenario.Id
                      ScenarioFamily = IntentAdmission.familyName scenario.ScenarioFamily
                      PromptIndex = index + 1
                      Text = text
                      ExpectedOutcome = string scenario.ExpectedOutcome
                      ExpectedFailedWitness = scenario.ExpectedFailedWitness }))

        { ManifestVersion = manifestVersion
          GeneratedAt = DateTimeOffset.UtcNow
          PromptCount = prompts.Length
          Prompts = prompts }

    let summarizeResults date (results: BenchmarkPromptResult list) =
        let promptCount = results.Length
        let totalAttempts = results |> List.sumBy (fun result -> result.AttemptCount)
        let parseSuccessCount = results |> List.filter (fun result -> result.ParseSuccess) |> List.length
        let tmSuccessCount = results |> List.filter (fun result -> result.TmTypeCheckSuccess) |> List.length
        let providerSuccessCount = results |> List.filter (fun result -> result.ProviderAdmissionSuccess) |> List.length
        let firstAttemptCount = results |> List.filter (fun result -> result.FirstAttemptSuccess) |> List.length
        let retryCount = results |> List.filter (fun result -> result.OneRetrySuccess) |> List.length
        let twoRetryCount = results |> List.filter (fun result -> result.ExpectationMatched && result.AttemptCount <= 3) |> List.length

        let rate count =
            if promptCount = 0 then 0.0 else float count / float promptCount

        let witnessCounts =
            results
            |> List.choose (fun result -> result.FirstFailedWitness)
            |> List.countBy id
            |> List.sortBy fst
            |> List.map (fun (witness, count) -> { Witness = witness; Count = count })

        { Model = results |> List.tryPick (fun result -> result.Model)
          PromptVersion = results |> List.tryPick (fun result -> result.PromptVersion)
          Date = date
          PromptCount = promptCount
          AttemptCount = totalAttempts
          ParseSuccessCount = parseSuccessCount
          ParseSuccessRate = rate parseSuccessCount
          TmTypeCheckSuccessCount = tmSuccessCount
          TmTypeCheckSuccessRate = rate tmSuccessCount
          ProviderAdmissionSuccessCount = providerSuccessCount
          ProviderAdmissionSuccessRate = rate providerSuccessCount
          FirstAttemptSuccessRate = rate firstAttemptCount
          OneRetrySuccessRate = rate retryCount
          TwoRetrySuccessRate = rate twoRetryCount
          FirstFailedWitnesses = witnessCounts }

    let private expectationMatched (prompt: BenchmarkPrompt) (record: IntentProcessingRecord) =
        let tmPassed = record.TmWitnessStatus = Some "passed"
        let providerPassed = record.AdmissionOutcome = Some "provider_admitted"
        let firstFailedWitness = record.FirstFailedWitness

        match prompt.ExpectedOutcome with
        | "DemoAccept" -> providerPassed && firstFailedWitness.IsNone
        | "DemoRejectTm" ->
            not tmPassed
            && (prompt.ExpectedFailedWitness.IsNone || prompt.ExpectedFailedWitness = firstFailedWitness)
        | "DemoRejectProvider" ->
            tmPassed
            && not providerPassed
            && (prompt.ExpectedFailedWitness.IsNone || prompt.ExpectedFailedWitness = firstFailedWitness)
        | _ -> false

    let private resultFromProcessingRecord (prompt: BenchmarkPrompt) (record: IntentProcessingRecord) =
        let attemptCount =
            record.LlmParse
            |> Option.map (fun metadata -> metadata.Attempts.Length)
            |> Option.defaultValue 0

        let expectationIsMatched = expectationMatched prompt record

        { PromptId = prompt.PromptId
          ScenarioId = prompt.ScenarioId
          ScenarioFamily = prompt.ScenarioFamily
          PromptIndex = prompt.PromptIndex
          Text = prompt.Text
          ExpectedOutcome = prompt.ExpectedOutcome
          ExpectedFailedWitness = prompt.ExpectedFailedWitness
          Model = record.LlmParse |> Option.bind (fun metadata -> metadata.Model)
          PromptVersion = record.LlmParse |> Option.bind (fun metadata -> metadata.PromptVersion)
          AttemptCount = attemptCount
          ParseSuccess = record.OperationalIntent.IsSome
          TmTypeCheckSuccess = record.TmWitnessStatus = Some "passed"
          ProviderAdmissionSuccess = record.AdmissionOutcome = Some "provider_admitted"
          SelectedProfile = record.SelectedProfile
          FirstFailedWitness = record.FirstFailedWitness
          AdmissionOutcome = record.AdmissionOutcome
          RequestId = record.RequestId
          ProcessingStatus = string record.Status
          ExpectationMatched = expectationIsMatched
          FirstAttemptSuccess = expectationIsMatched && attemptCount <= 1
          OneRetrySuccess = expectationIsMatched && attemptCount <= 2 }

    let private defaultRunDirectory () =
        let runId = "run-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        runId, Path.Combine(benchmarkRoot (), runId)

    let private defaultSyntheticCorrectnessRunDirectory () =
        let runId = "synthetic-correctness-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        runId, Path.Combine(syntheticCorrectnessRoot (), runId)

    let private createPrompt scenarioId promptIndex =
        let scenario =
            DemoScenarios.tryFindScenario scenarioId
            |> Option.defaultWith (fun () -> failwith $"Unknown scenario '{scenarioId}'")

        let variants = promptVariantsForScenario scenario

        if promptIndex < 1 || promptIndex > variants.Length then
            failwith $"Scenario '{scenarioId}' does not have prompt variant {promptIndex}."

        { PromptId = sprintf "%s_p%02i" scenario.Id promptIndex
          ScenarioId = scenario.Id
          ScenarioFamily = IntentAdmission.familyName scenario.ScenarioFamily
          PromptIndex = promptIndex
          Text = variants |> List.item (promptIndex - 1)
          ExpectedOutcome = string scenario.ExpectedOutcome
          ExpectedFailedWitness = scenario.ExpectedFailedWitness }

    let defaultSyntheticCorrectnessManifest repetitionCount expressionCount =
        let selectedPrompts =
            [ createPrompt "broadcast_success_01" 1
              createPrompt "broadcast_success_01" 2
              createPrompt "broadcast_success_01" 3
              createPrompt "broadcast_success_01" 4
              createPrompt "broadcast_success_01" 5
              createPrompt "critical_success_01" 1
              createPrompt "critical_success_01" 2
              createPrompt "critical_success_01" 3
              createPrompt "critical_success_01" 4
              createPrompt "critical_success_01" 5 ]
            |> List.truncate (max 1 expressionCount)

        { ManifestVersion = syntheticCorrectnessManifestVersion
          SuiteId = syntheticCorrectnessSuiteId
          GeneratedAt = DateTimeOffset.UtcNow
          ExpressionCount = selectedPrompts.Length
          RepetitionCount = max 1 repetitionCount
          AttemptBudget = 3
          Prompts = selectedPrompts }

    let private wilson95 successes trials =
        if trials <= 0 then
            0.0, 0.0
        else
            let z = 1.959963984540054
            let n = float trials
            let p = float successes / n
            let z2 = z * z
            let denominator = 1.0 + z2 / n
            let center = (p + z2 / (2.0 * n)) / denominator
            let margin =
                z
                * sqrt ((p * (1.0 - p) + z2 / (4.0 * n)) / n)
                / denominator

            max 0.0 (center - margin), min 1.0 (center + margin)

    let private rateSummary successes trials =
        let lower, upper = wilson95 successes trials

        { SuccessCount = successes
          TrialCount = trials
          SuccessRate =
            if trials = 0 then
                0.0
            else
                float successes / float trials
          Wilson95Lower = lower
          Wilson95Upper = upper }

    let private median (values: int list) =
        match values |> List.sort with
        | [] -> 0.0
        | xs ->
            let length = xs.Length
            let middle = length / 2

            if length % 2 = 1 then
                float xs[middle]
            else
                float (xs[middle - 1] + xs[middle]) / 2.0

    let private syntheticTrialResult prompt repetition (record: IntentProcessingRecord) =
        let result = resultFromProcessingRecord prompt record

        { TrialId = sprintf "r%02i_%s" repetition prompt.PromptId
          Repetition = repetition
          Result = result
          SuccessWithinSecondRetry = result.ExpectationMatched && result.AttemptCount <= 3
          LlmParse = record.LlmParse
          Artifacts = record.Artifacts
          Diagnostics = record.Diagnostics
          ProcessingRecordPath = "" }

    let summarizeSyntheticCorrectnessResults
        date
        (manifest: SyntheticCorrectnessManifest)
        (results: SyntheticCorrectnessTrialResult list) =
        let trialCount = results.Length
        let firstAttemptCount = results |> List.filter (fun result -> result.Result.FirstAttemptSuccess) |> List.length
        let firstRetryCount = results |> List.filter (fun result -> result.Result.OneRetrySuccess) |> List.length
        let secondRetryCount = results |> List.filter (fun result -> result.SuccessWithinSecondRetry) |> List.length
        let successfulAttempts =
            results
            |> List.choose (fun result ->
                if result.Result.ExpectationMatched then
                    Some result.Result.AttemptCount
                else
                    None)

        let attemptDistribution =
            successfulAttempts
            |> List.countBy id
            |> List.sortBy fst
            |> List.map (fun (attemptCount, count) ->
                { AttemptCount = attemptCount
                  Count = count })

        let witnessCounts =
            results
            |> List.choose (fun result -> result.Result.FirstFailedWitness)
            |> List.countBy id
            |> List.sortBy fst
            |> List.map (fun (witness, count) -> { Witness = witness; Count = count })

        let maxAttemptCountObserved =
            results
            |> List.map (fun result -> result.Result.AttemptCount)
            |> List.fold (fun current value -> max current value) 0

        let successfulTrialCount = successfulAttempts.Length

        { SuiteId = manifest.SuiteId
          ManifestVersion = manifest.ManifestVersion
          Model = results |> List.tryPick (fun result -> result.Result.Model)
          PromptVersion = results |> List.tryPick (fun result -> result.Result.PromptVersion)
          Date = date
          ExpressionCount = manifest.ExpressionCount
          RepetitionCount = manifest.RepetitionCount
          TrialCount = trialCount
          FirstAttempt = rateSummary firstAttemptCount trialCount
          FirstRetry = rateSummary firstRetryCount trialCount
          SecondRetry = rateSummary secondRetryCount trialCount
          SuccessfulTrials = successfulTrialCount
          FailureTrials = trialCount - successfulTrialCount
          SuccessAttemptDistribution = attemptDistribution
          MaxAttemptCountObserved = maxAttemptCountObserved
          MeanAttemptsOnSuccessfulTrials =
            if List.isEmpty successfulAttempts then
                0.0
            else
                successfulAttempts |> List.averageBy float
          MedianAttemptsOnSuccessfulTrials = median successfulAttempts
          FirstFailedWitnesses = witnessCounts }

    let runLiveAsync (rawIntentGenerator: IRawIntentGenerator) (outputDirectory: string option) =
        task {
            let manifest = defaultManifest ()

            let runId, runDirectory =
                match outputDirectory with
                | Some path when not (String.IsNullOrWhiteSpace path) ->
                    let normalized = Path.GetFullPath path
                    Path.GetFileName normalized, normalized
                | _ -> defaultRunDirectory ()

            Directory.CreateDirectory(runDirectory) |> ignore
            writeJson (Path.Combine(runDirectory, "manifest.json")) manifest

            let results = ResizeArray<BenchmarkPromptResult>()

            for prompt in manifest.Prompts do
                let request = DemoScenarios.buildNaturalLanguageRequest prompt.Text
                let intentId = $"benchmark-{prompt.PromptId}"
                let! outcome =
                    IntentPipeline.processIntentWithContextAsync
                        rawIntentGenerator
                        RawIntentGenerationContext.Live
                        intentId
                        request

                let result = resultFromProcessingRecord prompt outcome.ProcessingRecord
                results.Add result
                writeJson (Path.Combine(runDirectory, "results", $"{prompt.PromptId}.json")) result

            let finalizedResults = results |> Seq.toList
            let summary = summarizeResults DateTimeOffset.UtcNow finalizedResults
            let artifact : BenchmarkRunArtifact =
                { RunId = runId
                  Mode = "live"
                  Manifest = manifest
                  Results = finalizedResults
                  Summary = summary }

            writeJson (Path.Combine(runDirectory, "results.json")) finalizedResults
            writeJson (Path.Combine(runDirectory, "summary.json")) summary
            writeJson (Path.Combine(runDirectory, "run.json")) artifact

            return artifact
        }

    let replay runDirectory =
        let normalized = Path.GetFullPath runDirectory
        let manifestPath = Path.Combine(normalized, "manifest.json")
        let resultsPath = Path.Combine(normalized, "results.json")
        let manifest = readJson<BenchmarkManifest> manifestPath
        let results = readJson<BenchmarkPromptResult list> resultsPath
        let summary = summarizeResults DateTimeOffset.UtcNow results

        let artifact : BenchmarkRunArtifact =
            { RunId = Path.GetFileName normalized
              Mode = "replay"
              Manifest = manifest
              Results = results
              Summary = summary }

        writeJson (Path.Combine(normalized, "summary.replay.json")) summary
        writeJson (Path.Combine(normalized, "run.replay.json")) artifact
        artifact

    let runSyntheticCorrectnessAsync
        (rawIntentGenerator: IRawIntentGenerator)
        (outputDirectory: string option)
        repetitionCount
        expressionCount =
        task {
            let manifest = defaultSyntheticCorrectnessManifest repetitionCount expressionCount

            let runId, runDirectory =
                match outputDirectory with
                | Some path when not (String.IsNullOrWhiteSpace path) ->
                    let normalized = Path.GetFullPath path
                    Path.GetFileName normalized, normalized
                | _ -> defaultSyntheticCorrectnessRunDirectory ()

            Directory.CreateDirectory(runDirectory) |> ignore
            writeJson (Path.Combine(runDirectory, "manifest.json")) manifest

            let results = ResizeArray<SyntheticCorrectnessTrialResult>()

            for repetition in 1 .. manifest.RepetitionCount do
                for prompt in manifest.Prompts do
                    let request = DemoScenarios.buildNaturalLanguageRequest prompt.Text
                    let intentId = sprintf "synthetic-%s-r%02i" prompt.PromptId repetition
                    let! outcome =
                        IntentPipeline.processIntentWithContextAsync
                            rawIntentGenerator
                            RawIntentGenerationContext.Live
                            intentId
                            request

                    let result = syntheticTrialResult prompt repetition outcome.ProcessingRecord
                    let processingRecordPath = Path.Combine(runDirectory, "processing-records", $"{result.TrialId}.json")
                    let finalizedResult = { result with ProcessingRecordPath = processingRecordPath }
                    results.Add finalizedResult
                    writeJson (Path.Combine(runDirectory, "results", $"{finalizedResult.TrialId}.json")) finalizedResult
                    writeJson processingRecordPath outcome.ProcessingRecord

            let finalizedResults = results |> Seq.toList
            let summary = summarizeSyntheticCorrectnessResults DateTimeOffset.UtcNow manifest finalizedResults
            let artifact : SyntheticCorrectnessRunArtifact =
                { RunId = runId
                  Mode = "live_synthetic_correctness"
                  Manifest = manifest
                  Results = finalizedResults
                  Summary = summary }

            writeJson (Path.Combine(runDirectory, "results.json")) finalizedResults
            writeJson (Path.Combine(runDirectory, "summary.json")) summary
            writeJson (Path.Combine(runDirectory, "run.json")) artifact

            return artifact
        }
