namespace Tmf921.IntentManagement.Api

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

module DemoScenarios =
    type DemoExpectedOutcome =
        | DemoAccept
        | DemoRejectTm
        | DemoRejectProvider

    type ProviderProfile =
        | LiveBroadcastSilver
        | LiveBroadcastGold

    type ValidationIssue =
        { Code: string
          Message: string }

    type JsonBaselineResult =
        { Accepted: bool
          Issues: ValidationIssue list
          Dialect: string }

    type DependentTmResult =
        { Accepted: bool
          Issues: ValidationIssue list
          FailedWitness: string option }

    type DependentProviderResult =
        { Accepted: bool option
          SelectedProfile: string option
          Issues: ValidationIssue list
          FailedWitness: string option
          CheckerExcerpt: string option
          GeneratedModule: string option
          AdmissionTokenType: string option }

    type ConstraintStageResult =
        { Stage: string
          Witness: string
          Status: string
          Summary: string }

    type DemoTmIntent =
        { IntentName: string
          Venue: string option
          ServiceClass: string option
          EventDate: DateOnly option
          StartHour: int option
          EndHour: int option
          Timezone: string option
          DeviceCount: int option
          MaxUplinkLatencyMs: int option
          ReportingIntervalMinutes: int option
          ImmediateDegradationAlerts: bool
          PreserveEmergencyTraffic: bool
          RequestsPublicSafetyPreemption: bool
          OriginalText: string }

    type DemoProviderDecision =
        { SelectedProfile: string option
          Checks: ValidationIssue list }

    type DemoFStarCaseResult =
        { FileName: string
          ExpectedSuccess: bool
          ActualSuccess: bool
          Output: string }

    type DemoExpectationChecks =
        { JsonBaselineMatches: bool
          FailedWitnessMatches: bool
          DependentAgreement: bool
          Mismatches: ValidationIssue list }

    type DemoScenarioDefinition =
        { Id: string
          Title: string
          Kicker: string
          Text: string
          ExpectedOutcome: DemoExpectedOutcome
          ExpectedMessage: string
          ExpectedJsonAccepted: bool
          ExpectedFailedWitness: string option
          Story: string
          FStarFile: string option
          FStarExpectedSuccess: bool option }

    type DemoScenarioResult =
        { Id: string
          Title: string
          Kicker: string
          Text: string
          TmAccepted: bool
          TmIssues: ValidationIssue list
          NormalizedIntent: DemoTmIntent option
          ProviderAccepted: bool option
          ProviderDecision: DemoProviderDecision option
          ProviderFStarModule: string option
          FinalOutcome: string
          FStarCase: DemoFStarCaseResult option
          JsonBaseline: JsonBaselineResult
          DependentTm: DependentTmResult
          DependentProvider: DependentProviderResult
          ConstraintTrace: ConstraintStageResult list
          Story: string
          ExpectedJsonAccepted: bool
          ExpectedFailedWitness: string option
          ExpectationChecks: DemoExpectationChecks }

    type private GeneratedProviderFStarResult =
        { ActualSuccess: bool
          Output: string
          GeneratedModule: string
          AdmissionTokenType: string option }

    let private jsonBaselineDialect = "JSON Schema draft 2020-12"

    let private issue code message =
        { Code = code
          Message = message }

    let private regexGroup pattern text =
        let m = Regex.Match(text, pattern, RegexOptions.IgnoreCase)
        if m.Success then Some (m.Groups[1].Value.Trim()) else None

    let private regexInt pattern text =
        regexGroup pattern text
        |> Option.bind (fun value ->
            match Int32.TryParse value with
            | true, parsed -> Some parsed
            | _ -> None)

    let private parseDate text =
        regexGroup @"on\s+([A-Za-z]+\s+\d{1,2},\s+\d{4})" text
        |> Option.bind (fun value ->
            match DateOnly.TryParse value with
            | true, parsed -> Some parsed
            | _ -> None)

    let private parseTimezone text =
        regexGroup @"from\s+\d{1,2}:\d{2}\s+to\s+\d{1,2}:\d{2}\s+([A-Za-z_\/]+)" text

    let private parseHours text =
        let m = Regex.Match(text, @"from\s+(\d{1,2}):\d{2}\s+to\s+(\d{1,2}):\d{2}", RegexOptions.IgnoreCase)

        if m.Success then
            let okStart, startHour = Int32.TryParse m.Groups[1].Value
            let okEnd, endHour = Int32.TryParse m.Groups[2].Value

            if okStart && okEnd then
                Some(startHour, endHour)
            else
                None
        else
            None

    let private parseReportingInterval (text: string) =
        if Regex.IsMatch(text, @"hourly\s+compliance\s+updates", RegexOptions.IgnoreCase) then
            Some 60
        else
            regexInt @"every\s+(\d+)\s+minutes?" text

    let private inferServiceClass (text: string) =
        if Regex.IsMatch(text, @"premium\s+5G\s+broadcast\s+service", RegexOptions.IgnoreCase) then
            Some "premium-5g-broadcast"
        else
            None

    let private parseIntentName (venue: string option) =
        venue
        |> Option.map (fun value -> value.Replace(" ", String.Empty) + "LiveBroadcast")
        |> Option.defaultValue "LiveBroadcastIntent"

    let parseTextIntent text =
        let venue = regexGroup @"at\s+([A-Za-z ]+?)\s+on\s+[A-Za-z]+\s+\d{1,2},\s+\d{4}" text
        let date = parseDate text
        let hours = parseHours text
        let timezone = parseTimezone text
        let devices = regexInt @"support\s+up\s+to\s+(\d+)\s+production\s+devices" text
        let latency = regexInt @"latency\s+under\s+(\d+)\s*ms" text
        let interval = parseReportingInterval text

        let immediateAlerts =
            Regex.IsMatch(text, @"immediate\s+alerts?.+degrades?|immediate\s+alerts?.+degradation", RegexOptions.IgnoreCase)

        let preserveEmergency =
            Regex.IsMatch(text, @"do\s+not\s+impact\s+emergency-service\s+traffic", RegexOptions.IgnoreCase)

        let preemptSafety =
            Regex.IsMatch(text, @"preempt\s+reserved\s+public-safety\s+capacity", RegexOptions.IgnoreCase)

        let startHour, endHour =
            match hours with
            | Some(startHour, endHour) -> Some startHour, Some endHour
            | None -> None, None

        { IntentName = parseIntentName venue
          Venue = venue
          ServiceClass = inferServiceClass text
          EventDate = date
          StartHour = startHour
          EndHour = endHour
          Timezone = timezone
          DeviceCount = devices
          MaxUplinkLatencyMs = latency
          ReportingIntervalMinutes = interval
          ImmediateDegradationAlerts = immediateAlerts
          PreserveEmergencyTraffic = preserveEmergency
          RequestsPublicSafetyPreemption = preemptSafety
          OriginalText = text }

    let validateTmIntent (intent: DemoTmIntent) =
        let issues =
            [ if intent.Venue.IsNone then
                  issue "TM_MISSING_TARGET" "Intent is missing a recognizable venue target."
              if intent.ServiceClass.IsNone then
                  issue "TM_MISSING_SERVICE_CLASS" "Intent is missing a recognizable service class."
              if intent.EventDate.IsNone then
                  issue "TM_MISSING_DATE" "Intent is missing an event date."
              if intent.StartHour.IsNone || intent.EndHour.IsNone then
                  issue "TM_MISSING_WINDOW" "Intent is missing a usable time window."
              if intent.Timezone.IsNone then
                  issue "TM_MISSING_TIMEZONE" "Intent is missing a timezone."
              if intent.DeviceCount.IsNone then
                  issue "TM_MISSING_DEVICE_COUNT" "Intent is missing a measurable device-count expectation."
              if intent.MaxUplinkLatencyMs.IsNone then
                  issue "TM_MISSING_LATENCY" "Intent is missing a measurable uplink latency expectation."
              if intent.ReportingIntervalMinutes.IsNone then
                  issue "TM_MISSING_REPORTING" "Intent is missing a reporting interval."
              if not intent.ImmediateDegradationAlerts then
                  issue "TM_MISSING_ALERT_POLICY" "Intent is missing an immediate degradation alert expectation." ]

        match intent.StartHour, intent.EndHour with
        | Some startHour, Some endHour when startHour >= endHour ->
            issues
            @ [ issue "TM_INVALID_WINDOW" "Intent time window is invalid because the start is not before the end." ]
        | _ -> issues

    let private profileForVenue venue =
        match venue with
        | "Detroit Stadium" -> Some LiveBroadcastGold
        | "Metro Arena" -> Some LiveBroadcastSilver
        | _ -> None

    let private profileName profile =
        match profile with
        | LiveBroadcastSilver -> "LiveBroadcastSilver"
        | LiveBroadcastGold -> "LiveBroadcastGold"

    let private profileConstructor profile =
        match profile with
        | LiveBroadcastSilver -> "LiveBroadcastSilver"
        | LiveBroadcastGold -> "LiveBroadcastGold"

    let private profileFromIntent intent =
        intent.Venue |> Option.bind profileForVenue

    let private maxDevices profile =
        match profile with
        | LiveBroadcastSilver -> 100
        | LiveBroadcastGold -> 250

    let private minLatencyBound profile =
        match profile with
        | LiveBroadcastSilver -> 40
        | LiveBroadcastGold -> 20

    let private providerWindowOk intent =
        match intent.StartHour, intent.EndHour with
        | Some startHour, Some endHour -> startHour >= 6 && endHour <= 23
        | _ -> false

    let private reportingOk intent =
        match intent.ReportingIntervalMinutes with
        | Some minutes -> minutes >= 15
        | None -> false

    let validateProviderIntent (intent: DemoTmIntent) =
        match intent.Venue with
        | None ->
            { SelectedProfile = None
              Checks = [ issue "PROVIDER_NO_VENUE" "Provider validation requires a known venue." ] }
        | Some venue ->
            match profileForVenue venue with
            | None ->
                { SelectedProfile = None
                  Checks = [ issue "PROVIDER_UNKNOWN_VENUE" $"Venue '{venue}' is not supported by the provider demo model." ] }
            | Some profile ->
                let checks =
                    [ match intent.DeviceCount with
                      | Some devices when devices <= maxDevices profile -> ()
                      | Some devices ->
                          yield
                              issue
                                  "PROVIDER_DEVICE_COUNT"
                                  $"Requested device count {devices} exceeds the {profileName profile} capacity limit of {maxDevices profile}."
                      | None -> yield issue "PROVIDER_MISSING_DEVICE_COUNT" "Device count is required for provider validation."

                      match intent.MaxUplinkLatencyMs with
                      | Some latency when latency >= minLatencyBound profile -> ()
                      | Some latency ->
                          yield
                              issue
                                  "PROVIDER_LATENCY_BOUND"
                                  $"Requested uplink latency under {latency} ms is below the admissible {minLatencyBound profile} ms bound for {profileName profile}."
                      | None -> yield issue "PROVIDER_MISSING_LATENCY" "Latency bound is required for provider validation."

                      match intent.StartHour, intent.EndHour with
                      | Some startHour, Some endHour when startHour >= 6 && endHour <= 23 -> ()
                      | Some _, Some _ ->
                          yield
                              issue
                                  "PROVIDER_WINDOW"
                                  "Requested booking window is outside the provider's allowed operating window of 06:00 to 23:00."
                      | _ -> yield issue "PROVIDER_MISSING_WINDOW" "Booking window is required for provider validation."

                      match intent.ReportingIntervalMinutes with
                      | Some minutes when minutes >= 15 -> ()
                      | Some minutes ->
                          yield
                              issue
                                  "PROVIDER_REPORTING"
                                  $"Requested reporting interval of {minutes} minutes is below the provider minimum of 15 minutes."
                      | None -> yield issue "PROVIDER_MISSING_REPORTING" "Reporting interval is required for provider validation."

                      if intent.RequestsPublicSafetyPreemption then
                          yield
                              issue
                                  "PROVIDER_PROTECTED_TRAFFIC"
                                  "Requested policy would preempt protected public-safety capacity."

                      if not intent.PreserveEmergencyTraffic then
                          yield
                              issue
                                  "PROVIDER_EMERGENCY_TRAFFIC"
                                  "Intent must explicitly preserve emergency-service traffic." ]

                { SelectedProfile = Some(profileName profile)
                  Checks = checks }

    let private tmFailedWitness (intent: DemoTmIntent) =
        let measurableMissing =
            intent.Venue.IsNone
            || intent.ServiceClass.IsNone
            || intent.EventDate.IsNone
            || intent.StartHour.IsNone
            || intent.EndHour.IsNone
            || intent.Timezone.IsNone
            || intent.DeviceCount.IsNone
            || intent.MaxUplinkLatencyMs.IsNone
            || intent.ReportingIntervalMinutes.IsNone

        if measurableMissing then
            Some "measurable_intent"
        else
            match intent.StartHour, intent.EndHour with
            | Some startHour, Some endHour when startHour >= endHour -> Some "window_checked_intent"
            | _ when not intent.ImmediateDegradationAlerts -> Some "tm_checked_intent"
            | _ -> None

    let private providerFailedWitness (intent: DemoTmIntent) =
        match tmFailedWitness intent with
        | Some _ -> None
        | None ->
            match profileFromIntent intent with
            | None -> Some "profiled_intent"
            | Some profile ->
                let capacityStageFails =
                    not (providerWindowOk intent)
                    || not (reportingOk intent)
                    ||
                       match intent.DeviceCount with
                       | Some devices -> devices > maxDevices profile
                       | None -> true

                let latencyStageFails =
                    match intent.MaxUplinkLatencyMs with
                    | Some latency -> latency < minLatencyBound profile
                    | None -> true

                if capacityStageFails then
                    Some "capacity_checked_intent"
                else if latencyStageFails then
                    Some "latency_checked_intent"
                else if intent.RequestsPublicSafetyPreemption || not intent.PreserveEmergencyTraffic then
                    Some "policy_checked_intent"
                else
                    None

    let private demoProjectDir () =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))

    let private demoSchemaPath () =
        Path.Combine(demoProjectDir (), "DemoSchemas", "BroadcastIntent.schema.json")

    let private fstarDemoDir () =
        Path.Combine(demoProjectDir (), "FStarDemo")

    let private jsonBaselineSchema =
        lazy
            let schemaText = File.ReadAllText(demoSchemaPath ())
            Json.Schema.JsonSchema.FromText(schemaText)

    let private buildJsonBaselineInstance (intent: DemoTmIntent) =
        JsonSerializer.SerializeToElement(
            {| intentName = intent.IntentName
               venue = intent.Venue
               serviceClass = intent.ServiceClass
               eventDate =
                intent.EventDate
                |> Option.map (fun value -> value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
               startHour = intent.StartHour
               endHour = intent.EndHour
               timezone = intent.Timezone
               deviceCount = intent.DeviceCount
               maxUplinkLatencyMs = intent.MaxUplinkLatencyMs
               reportingIntervalMinutes = intent.ReportingIntervalMinutes
               immediateDegradationAlerts = intent.ImmediateDegradationAlerts
               preserveEmergencyTraffic = intent.PreserveEmergencyTraffic
               requestsPublicSafetyPreemption = intent.RequestsPublicSafetyPreemption |})

    let rec private collectJsonSchemaIssues (element: JsonElement) =
        let nested =
            match element.ValueKind with
            | JsonValueKind.Object ->
                let mutable detailsProperty = Unchecked.defaultof<JsonElement>
                let hasDetails = element.TryGetProperty("details", &detailsProperty)

                if hasDetails && detailsProperty.ValueKind = JsonValueKind.Array then
                    detailsProperty.EnumerateArray()
                    |> Seq.toList
                    |> List.collect collectJsonSchemaIssues
                else
                    []
            | _ -> []

        let local =
            match element.ValueKind with
            | JsonValueKind.Object ->
                let mutable errorsProperty = Unchecked.defaultof<JsonElement>
                let hasErrors = element.TryGetProperty("errors", &errorsProperty)

                if hasErrors && errorsProperty.ValueKind = JsonValueKind.Object then
                    let mutable instanceLocationProperty = Unchecked.defaultof<JsonElement>
                    let hasInstanceLocation = element.TryGetProperty("instanceLocation", &instanceLocationProperty)

                    let instanceLocation =
                        if hasInstanceLocation then
                            instanceLocationProperty.GetString() |> Option.ofObj |> Option.defaultValue "$"
                        else
                            "$"

                    errorsProperty.EnumerateObject()
                    |> Seq.toList
                    |> List.map (fun entry ->
                        issue
                            $"JSON_{entry.Name.ToUpperInvariant()}"
                            $"{entry.Value.ToString()} (instance: {instanceLocation})")
                else
                    []
            | _ -> []

        local @ nested

    let private validateJsonBaseline intent =
        let options = Json.Schema.EvaluationOptions()
        options.OutputFormat <- Json.Schema.OutputFormat.List
        options.RequireFormatValidation <- true

        let result = jsonBaselineSchema.Value.Evaluate(buildJsonBaselineInstance intent, options)
        let resultJson = JsonSerializer.SerializeToElement(result.ToList())

        let issues =
            if result.IsValid then
                []
            else
                let collected =
                    collectJsonSchemaIssues resultJson
                    |> List.distinctBy (fun value -> value.Code, value.Message)

                if collected.IsEmpty then
                    [ issue "JSON_BASELINE_REJECTED" "The normalized JSON shape failed the baseline schema." ]
                else
                    collected

        { Accepted = result.IsValid
          Issues = issues
          Dialect = jsonBaselineDialect }

    let private stage stage witness status summary =
        { Stage = stage
          Witness = witness
          Status = status
          Summary = summary }

    let private runProcess fileName arguments =
        let info = ProcessStartInfo()
        info.FileName <- fileName
        info.Arguments <- arguments
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.UseShellExecute <- false

        use proc = new Process()
        proc.StartInfo <- info
        proc.Start() |> ignore

        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        proc.ExitCode, stdout, stderr

    let private combineOutput (stdout: string) (stderr: string) =
        [ stdout.Trim(); stderr.Trim() ]
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> String.concat "\n"

    let private excerpt (text: string) =
        if String.IsNullOrWhiteSpace text then
            None
        else if text.Length <= 2000 then
            Some text
        else
            Some(text.Substring(0, 2000) + "\n... output truncated ...")

    let private runFStarCase fileName expectedSuccess =
        let baseDir = fstarDemoDir ()
        let filePath = Path.Combine(baseDir, fileName)
        let arguments = $"--include \"{baseDir}\" \"{filePath}\""
        let exitCode, stdout, stderr = runProcess "fstar.exe" arguments

        { FileName = fileName
          ExpectedSuccess = expectedSuccess
          ActualSuccess = exitCode = 0
          Output = combineOutput stdout stderr }

    let private fstarString (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")

    let private renderOptionString (value: string option) =
        match value with
        | Some text -> $"Some \"{fstarString text}\""
        | None -> "None"

    let private renderOptionNat (value: int option) =
        match value with
        | Some number when number >= 0 -> $"Some {number}"
        | _ -> "None"

    let private renderVenueOption (venue: string option) =
        match venue with
        | Some "Detroit Stadium" -> "Some DetroitStadium"
        | Some "Metro Arena" -> "Some MetroArena"
        | Some other -> $"Some (OtherVenue \"{fstarString other}\")"
        | None -> "None"

    let private selectedProfileConstructor (intent: DemoTmIntent) =
        match profileFromIntent intent with
        | Some profile -> profileConstructor profile
        | None -> "UnsupportedProfile"

    let private sanitizeModuleSegment (value: string) =
        let sanitized = Regex.Replace(value, "[^A-Za-z0-9_]", "")
        if String.IsNullOrWhiteSpace sanitized then "Intent" else sanitized

    let private renderProviderIntentRecord (intentName: string) (intent: DemoTmIntent) =
        let monthText, dayValue, yearValue =
            match intent.EventDate with
            | Some date ->
                Some(date.ToString("MMMM", CultureInfo.InvariantCulture)), Some date.Day, Some date.Year
            | None -> None, None, None

        $"""let {intentName} : tm_intent =
  {{ intent_name = "{fstarString intent.IntentName}";
    venue = {renderVenueOption intent.Venue};
    service_class = {renderOptionString intent.ServiceClass};
    event_month = {renderOptionString monthText};
    event_day = {renderOptionNat dayValue};
    event_year = {renderOptionNat yearValue};
    start_hour = {renderOptionNat intent.StartHour};
    end_hour = {renderOptionNat intent.EndHour};
    timezone = {renderOptionString intent.Timezone};
    device_count = {renderOptionNat intent.DeviceCount};
    max_uplink_latency_ms = {renderOptionNat intent.MaxUplinkLatencyMs};
    reporting_interval_minutes = {renderOptionNat intent.ReportingIntervalMinutes};
    immediate_degradation_alerts = {string intent.ImmediateDegradationAlerts |> fun value -> value.ToLowerInvariant()};
    preserve_emergency_traffic = {string intent.PreserveEmergencyTraffic |> fun value -> value.ToLowerInvariant()};
    request_public_safety_preemption = {string intent.RequestsPublicSafetyPreemption |> fun value -> value.ToLowerInvariant()} }}"""

    let private buildProviderFStarModule moduleName (intent: DemoTmIntent) =
        let intentBindingName = "demo_intent"

        $"""module {moduleName}

open BroadcastProviderDemo

{renderProviderIntentRecord intentBindingName intent}

let selected_profile : profile =
  {selectedProfileConstructor intent}

let measurable : measurable_intent {intentBindingName} =
  mk_measurable {intentBindingName}

let window_checked : window_checked_intent {intentBindingName} =
  mk_window_checked {intentBindingName}

let tm_checked : tm_checked_intent {intentBindingName} =
  mk_tm_checked {intentBindingName}

let profiled : profiled_intent selected_profile {intentBindingName} =
  mk_profiled selected_profile {intentBindingName}

let capacity_checked : capacity_checked_intent selected_profile {intentBindingName} =
  mk_capacity_checked selected_profile {intentBindingName}

let latency_checked : latency_checked_intent selected_profile {intentBindingName} =
  mk_latency_checked selected_profile {intentBindingName}

let policy_checked : policy_checked_intent selected_profile {intentBindingName} =
  mk_policy_checked selected_profile {intentBindingName}

let provider_checked : provider_checked_intent selected_profile {intentBindingName} =
  mk_provider_checked selected_profile {intentBindingName}

let admission_token_for_demo : admission_token selected_profile =
  issue_admission_token selected_profile {intentBindingName} provider_checked
"""

    let private runGeneratedProviderFStar moduleKey intent =
        let baseDir = fstarDemoDir ()
        let moduleName = $"BroadcastProviderDemo.Generated{sanitizeModuleSegment moduleKey}"
        let filePath = Path.Combine(baseDir, $"{moduleName}.fst")
        let moduleText = buildProviderFStarModule moduleName intent

        File.WriteAllText(filePath, moduleText, Encoding.UTF8)

        let arguments = $"--include \"{baseDir}\" \"{filePath}\""
        let exitCode, stdout, stderr = runProcess "fstar.exe" arguments
        let output = combineOutput stdout stderr

        { ActualSuccess = exitCode = 0
          Output = output
          GeneratedModule = moduleText
          AdmissionTokenType =
            match exitCode = 0, profileFromIntent intent with
            | true, Some profile -> Some $"admission_token {profileName profile}"
            | _ -> None }

    let private buildConstraintTrace
        (intent: DemoTmIntent)
        (jsonBaseline: JsonBaselineResult)
        (dependentTm: DependentTmResult)
        (dependentProvider: DependentProviderResult)
        (providerExpectedAccepted: bool) =
        let tmFailure = dependentTm.FailedWitness
        let providerFailure = dependentProvider.FailedWitness
        let providerSkipped = dependentProvider.Accepted.IsNone
        let providerAccepted = dependentProvider.Accepted = Some true

        let tmStages =
            [ match tmFailure with
              | Some "measurable_intent" ->
                  stage "TM measurability" "measurable_intent" "failed" "Required measurable fields are still missing."
                  stage "Window sanity" "window_checked_intent" "skipped" "Window checking never ran because measurability failed."
                  stage "TM witness" "tm_checked_intent" "skipped" "TM witness construction depends on the earlier stages."
              | Some "window_checked_intent" ->
                  stage "TM measurability" "measurable_intent" "passed" "All measurable fields are present."
                  stage "Window sanity" "window_checked_intent" "failed" "The stated time window is not strictly increasing."
                  stage "TM witness" "tm_checked_intent" "skipped" "TM witness construction stops at the invalid window."
              | Some "tm_checked_intent" ->
                  stage "TM measurability" "measurable_intent" "passed" "All measurable fields are present."
                  stage "Window sanity" "window_checked_intent" "passed" "The time window is structurally usable."
                  stage "TM witness" "tm_checked_intent" "failed" "TM intent obligations are incomplete."
              | _ ->
                  stage "TM measurability" "measurable_intent" "passed" "All measurable fields are present."
                  stage "Window sanity" "window_checked_intent" "passed" "The time window is structurally usable."
                  stage "TM witness" "tm_checked_intent" "passed" "A TM-level witness can be constructed." ]

        let providerStages =
            if providerSkipped then
                [ stage "Profile resolution" "profiled_intent" "skipped" "Provider reasoning is skipped until the TM witness exists."
                  stage "Capacity and operating envelope" "capacity_checked_intent" "skipped" "Provider constraints depend on a TM witness."
                  stage "Latency floor" "latency_checked_intent" "skipped" "Latency refinement depends on earlier provider stages."
                  stage "Safety policy" "policy_checked_intent" "skipped" "Policy refinement depends on earlier provider stages."
                  stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission is not attempted."
                  stage "Admission token" "admission_token" "skipped" "No downstream token can be issued." ]
            else
                [ match providerFailure with
                  | Some "profiled_intent" ->
                      stage "Profile resolution" "profiled_intent" "failed" "The normalized venue does not map to a supported provider profile."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "skipped" "Capacity checking depends on a resolved provider profile."
                      stage "Latency floor" "latency_checked_intent" "skipped" "Latency refinement depends on earlier provider stages."
                      stage "Safety policy" "policy_checked_intent" "skipped" "Policy refinement depends on earlier provider stages."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at profile resolution."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | Some "capacity_checked_intent" ->
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "failed" "Capacity, reporting, or provider operating-window constraints were violated."
                      stage "Latency floor" "latency_checked_intent" "skipped" "Latency refinement depends on the capacity stage."
                      stage "Safety policy" "policy_checked_intent" "skipped" "Policy refinement depends on earlier provider stages."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at the capacity stage."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | Some "latency_checked_intent" ->
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "failed" "The request undercuts the profile-specific latency floor."
                      stage "Safety policy" "policy_checked_intent" "skipped" "Policy refinement depends on the latency stage."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at the latency stage."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | Some "policy_checked_intent" ->
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "passed" "The request satisfies the profile-specific latency floor."
                      stage "Safety policy" "policy_checked_intent" "failed" "Protected-traffic and emergency-traffic constraints were violated."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at the policy stage."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | _ when providerAccepted ->
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "passed" "The request satisfies the profile-specific latency floor."
                      stage "Safety policy" "policy_checked_intent" "passed" "The request preserves protected traffic."
                      stage "Provider admission" "provider_checked_intent" "passed" "A provider-level witness has been constructed."
                      stage "Admission token" "admission_token" "passed" "A downstream typed admission token is now constructible."
                  | _ ->
                      let providerSummary =
                          if providerExpectedAccepted then
                              "F* rejected a request that the explainer path expected to admit."
                          else
                              "F* rejected the request at provider admission."

                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "passed" "The request satisfies the profile-specific latency floor."
                      stage "Safety policy" "policy_checked_intent" "passed" "The request preserves protected traffic."
                      stage "Provider admission" "provider_checked_intent" "failed" providerSummary
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued." ]

        stage "JSON baseline" "json_schema_draft_2020_12" (if jsonBaseline.Accepted then "passed" else "failed")
            (if jsonBaseline.Accepted then
                 "The normalized JSON shape satisfies the baseline schema."
             else
                 "The normalized JSON shape fails basic schema validation.")
        :: (tmStages @ providerStages)

    let private inferStory
        (definition: DemoScenarioDefinition option)
        (jsonBaseline: JsonBaselineResult)
        (dependentTm: DependentTmResult)
        (dependentProvider: DependentProviderResult) =
        match definition with
        | Some scenario -> scenario.Story
        | None ->
            if not jsonBaseline.Accepted then
                "Even a schema-only baseline rejects this request because the normalized shape is still incomplete."
            else if not dependentTm.Accepted then
                "The JSON shape passes, but the TM witness cannot be composed because business-level intent requirements are still unmet."
            else
                match dependentProvider.Accepted with
                | Some true ->
                    "The request survives shape checks and composes all the way to a typed admission token."
                | Some false ->
                    let failedWitness =
                        dependentProvider.FailedWitness |> Option.defaultValue "provider_checked_intent"

                    $"The JSON shape passes, but the provider witness chain breaks at {failedWitness}, which exposes domain semantics beyond schema validation."
                | None ->
                    "Provider reasoning stays dormant until the TM witness exists."

    let private expectationChecks
        (definition: DemoScenarioDefinition)
        (jsonBaseline: JsonBaselineResult)
        (dependentTm: DependentTmResult)
        (dependentProvider: DependentProviderResult) =
        let actualFailedWitness =
            dependentTm.FailedWitness |> Option.orElse dependentProvider.FailedWitness

        let providerAgreement =
            match dependentProvider.Accepted with
            | Some accepted ->
                let expectedProviderAccept = dependentProvider.FailedWitness.IsNone
                accepted = expectedProviderAccept
            | None -> true

        let expectedFailedWitnessText =
            definition.ExpectedFailedWitness |> Option.defaultValue "<none>"

        let actualFailedWitnessText =
            actualFailedWitness |> Option.defaultValue "<none>"

        let mismatches =
            [ if jsonBaseline.Accepted <> definition.ExpectedJsonAccepted then
                  yield
                      issue
                          "EXPECT_JSON_BASELINE"
                          $"Expected jsonBaseline.accepted = {definition.ExpectedJsonAccepted}, but got {jsonBaseline.Accepted}."

              if actualFailedWitness <> definition.ExpectedFailedWitness then
                  yield
                      issue
                          "EXPECT_FAILED_WITNESS"
                          $"Expected failed witness = {expectedFailedWitnessText}, but got {actualFailedWitnessText}."

              if not providerAgreement then
                  yield
                      issue
                          "EXPECT_DEPENDENT_AGREEMENT"
                          "The F* provider checker disagreed with the explainer path about whether a provider witness should exist." ]

        { JsonBaselineMatches = jsonBaseline.Accepted = definition.ExpectedJsonAccepted
          FailedWitnessMatches = actualFailedWitness = definition.ExpectedFailedWitness
          DependentAgreement = providerAgreement
          Mismatches = mismatches }

    let private evaluateScenario scenarioId definition text =
        let normalized = parseTextIntent text
        let tmIssues = validateTmIntent normalized
        let providerDecision = if tmIssues.IsEmpty then Some(validateProviderIntent normalized) else None
        let providerAccepted = providerDecision |> Option.map (fun decision -> decision.Checks.IsEmpty)
        let jsonBaseline = validateJsonBaseline normalized
        let dependentTmWitnessFailure = tmFailedWitness normalized

        let dependentTm =
            { Accepted = dependentTmWitnessFailure.IsNone
              Issues = tmIssues
              FailedWitness = dependentTmWitnessFailure }

        let generatedProvider = runGeneratedProviderFStar scenarioId normalized
        let providerWitnessFailure = providerFailedWitness normalized

        let dependentProviderAccepted =
            if dependentTm.Accepted then
                Some generatedProvider.ActualSuccess
            else
                None

        let mismatchIssues =
            match dependentProviderAccepted with
            | Some accepted when accepted <> providerWitnessFailure.IsNone ->
                [ issue
                      "DEPENDENT_PROVIDER_MISMATCH"
                      "The F* checker disagreed with the explainer path about provider admission. Inspect the checker excerpt in the audit details." ]
            | _ -> []

        let dependentProviderIssues =
            (providerDecision |> Option.map (fun decision -> decision.Checks) |> Option.defaultValue [])
            @ mismatchIssues

        let dependentProvider =
            { Accepted = dependentProviderAccepted
              SelectedProfile = providerDecision |> Option.bind (fun decision -> decision.SelectedProfile)
              Issues = dependentProviderIssues
              FailedWitness = if dependentTm.Accepted then providerWitnessFailure else None
              CheckerExcerpt = excerpt generatedProvider.Output
              GeneratedModule = Some generatedProvider.GeneratedModule
              AdmissionTokenType = generatedProvider.AdmissionTokenType }

        let finalOutcome =
            if not dependentTm.Accepted then
                "rejected_tm"
            else
                match dependentProvider.Accepted with
                | Some true -> "accepted"
                | _ -> "rejected_provider"

        let fstarResult =
            match definition.FStarFile, definition.FStarExpectedSuccess with
            | Some fileName, Some expectedSuccess -> Some(runFStarCase fileName expectedSuccess)
            | _ -> None

        let story = inferStory (Some definition) jsonBaseline dependentTm dependentProvider
        let checks = expectationChecks definition jsonBaseline dependentTm dependentProvider

        { Id = definition.Id
          Title = definition.Title
          Kicker = definition.Kicker
          Text = text
          TmAccepted = dependentTm.Accepted
          TmIssues = tmIssues
          NormalizedIntent = Some normalized
          ProviderAccepted = dependentProvider.Accepted
          ProviderDecision = providerDecision
          ProviderFStarModule = dependentProvider.GeneratedModule
          FinalOutcome = finalOutcome
          FStarCase = fstarResult
          JsonBaseline = jsonBaseline
          DependentTm = dependentTm
          DependentProvider = dependentProvider
          ConstraintTrace = buildConstraintTrace normalized jsonBaseline dependentTm dependentProvider (providerWitnessFailure.IsNone)
          Story = story
          ExpectedJsonAccepted = definition.ExpectedJsonAccepted
          ExpectedFailedWitness = definition.ExpectedFailedWitness
          ExpectationChecks = checks }

    let private adHocDefinition text =
        { Id = "ad_hoc"
          Title = "Ad Hoc Statement"
          Kicker = "Interactive"
          Text = text
          ExpectedOutcome = DemoAccept
          ExpectedMessage = "Ad hoc validation request."
          ExpectedJsonAccepted = false
          ExpectedFailedWitness = None
          Story = ""
          FStarFile = None
          FStarExpectedSuccess = None }

    let scenarios =
        [ { Id = "broadcast_success_01"
            Title = "Accepted Broadcast"
            Kicker = "Scenario 1"
            Text =
              "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoAccept
            ExpectedMessage = "Accepted with LiveBroadcastGold and a downstream admission token."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = None
            Story =
              "This is the full success path: the JSON shape passes, every witness composes, and the demo can construct a typed admission token rather than stopping at pass/fail validation."
            FStarFile = Some "BroadcastProviderDemo.Success.fst"
            FStarExpectedSuccess = Some true }
          { Id = "broadcast_fail_window_01"
            Title = "Window Witness Failure"
            Kicker = "Scenario 2"
            Text =
              "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 22:00 to 18:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectTm
            ExpectedMessage = "JSON shape passes, but the TM window witness cannot be constructed."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "window_checked_intent"
            Story =
              "This case is structurally complete enough for JSON validation, yet the dependent TM model still rejects it because the time window is semantically impossible."
            FStarFile = Some "BroadcastProviderDemo.FailWindow.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_capacity_01"
            Title = "Capacity Witness Failure"
            Kicker = "Scenario 3"
            Text =
              "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 2000 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "JSON shape passes, but provider capacity constraints fail."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "capacity_checked_intent"
            Story =
              "A schema can confirm that deviceCount is an integer, but only the typed provider model can state that the selected venue profile cannot compose a capacity witness for 2000 devices."
            FStarFile = Some "BroadcastProviderDemo.FailCapacity.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_latency_01"
            Title = "Latency Witness Failure"
            Kicker = "Scenario 4"
            Text =
              "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 5 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "JSON shape passes, but the provider latency-floor witness fails."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "latency_checked_intent"
            Story =
              "The request is perfectly well-formed JSON, yet dependent types expose that the same shape cannot inhabit the latency witness required by the Gold venue profile."
            FStarFile = Some "BroadcastProviderDemo.FailLatency.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_policy_01"
            Title = "Policy Witness Failure"
            Kicker = "Scenario 5"
            Text =
              "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. If needed, preempt reserved public-safety capacity to maintain service."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "JSON shape passes, but the policy witness fails."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "policy_checked_intent"
            Story =
              "The schema baseline has no notion of protected traffic. The dependent provider model makes that policy explicit, so the witness chain breaks before an admission token can exist."
            FStarFile = Some "BroadcastProviderDemo.FailPolicy.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_profile_gold_01"
            Title = "Gold Profile Accepts"
            Kicker = "Scenario 6"
            Text =
              "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 90 production devices. Keep uplink latency under 30 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoAccept
            ExpectedMessage = "The Gold profile admits this shape and issues a token."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = None
            Story =
              "This request is admissible when the same JSON shape resolves to the Gold profile, which lets the demo show business semantics living in the typed profile, not in the raw JSON alone."
            FStarFile = Some "BroadcastProviderDemo.ProfileGold.fst"
            FStarExpectedSuccess = Some true }
          { Id = "broadcast_profile_silver_01"
            Title = "Silver Profile Rejects Same Shape"
            Kicker = "Scenario 7"
            Text =
              "Provide a premium 5G broadcast service for the live event at Metro Arena on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 90 production devices. Keep uplink latency under 30 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "The same JSON shape fails once the provider profile changes."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "latency_checked_intent"
            Story =
              "Only the venue-to-profile mapping changes here. JSON validation still passes, but the Silver profile requires a looser latency floor, so the dependent witness chain rejects the same shape."
            FStarFile = Some "BroadcastProviderDemo.ProfileSilver.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_tm_01"
            Title = "Shape-Level Failure"
            Kicker = "Scenario 8"
            Text = "Make the event network really good and fast for the broadcast."
            ExpectedOutcome = DemoRejectTm
            ExpectedMessage = "The request is too underspecified even for the JSON baseline."
            ExpectedJsonAccepted = false
            ExpectedFailedWitness = Some "measurable_intent"
            Story =
              "This is the easy case for a schema baseline: the request never becomes a measurable normalized object, so both JSON shape checks and dependent witnesses stop immediately."
            FStarFile = Some "BroadcastProviderDemo.FailTm.fst"
            FStarExpectedSuccess = Some false } ]

    let featuredScenarioIds =
        [ "broadcast_success_01"
          "broadcast_fail_window_01"
          "broadcast_fail_capacity_01"
          "broadcast_fail_latency_01"
          "broadcast_fail_policy_01"
          "broadcast_profile_gold_01"
          "broadcast_profile_silver_01"
          "broadcast_fail_tm_01" ]

    let tryFindScenario id =
        scenarios |> List.tryFind (fun scenario -> scenario.Id = id)

    let featuredScenarios =
        featuredScenarioIds |> List.choose tryFindScenario

    let tryReadFStarSource fileName =
        let filePath = Path.Combine(fstarDemoDir (), fileName)
        if File.Exists filePath then Some(File.ReadAllText filePath) else None

    let runScenario (definition: DemoScenarioDefinition) : DemoScenarioResult =
        evaluateScenario definition.Id definition definition.Text

    let validateScenarioText (definition: DemoScenarioDefinition) (text: string) : DemoScenarioResult =
        let adjusted = { definition with Text = text }
        evaluateScenario definition.Id adjusted text

    let validateText (text: string) : DemoScenarioResult =
        let definition = adHocDefinition text
        let result = evaluateScenario "AdHocIntent" definition text
        let story = inferStory None result.JsonBaseline result.DependentTm result.DependentProvider

        { result with
            Story = story
            ExpectedJsonAccepted = result.JsonBaseline.Accepted
            ExpectedFailedWitness = result.DependentTm.FailedWitness |> Option.orElse result.DependentProvider.FailedWitness
            ExpectationChecks =
                { JsonBaselineMatches = true
                  FailedWitnessMatches = true
                  DependentAgreement = true
                  Mismatches = [] } }

    let buildNaturalLanguageRequest (text: string) : IntentFvo =
        let expressionValue = JsonDocument.Parse(JsonSerializer.Serialize(text)).RootElement.Clone()

        { Name = "demoIntent"
          Expression =
            { Iri = "urn:tmf921:demo:nl"
              ExpressionValue = expressionValue
              Type = Some "NaturalLanguageExpression"
              BaseType = None
              SchemaLocation = None }
          Description = Some "Natural-language validation request from the demo page."
          ValidFor = None
          IsBundle = None
          Priority = Some "1"
          Context = Some "Live broadcast service"
          Version = Some "1.0"
          IntentSpecification = None
          LifecycleStatus = Some "acknowledged"
          Type = Some "Intent"
          BaseType = None
          SchemaLocation = None }

    let runAll () =
        scenarios |> List.map runScenario
