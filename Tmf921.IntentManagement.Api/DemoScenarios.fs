namespace Tmf921.IntentManagement.Api

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text.RegularExpressions

module DemoScenarios =
    open System.Text.Json

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

    type DemoScenarioDefinition =
        { Id: string
          Text: string
          ExpectedOutcome: DemoExpectedOutcome
          ExpectedMessage: string
          FStarFile: string option
          FStarExpectedSuccess: bool option }

    type DemoScenarioResult =
        { Id: string
          Text: string
          TmAccepted: bool
          TmIssues: ValidationIssue list
          NormalizedIntent: DemoTmIntent option
          ProviderAccepted: bool option
          ProviderDecision: DemoProviderDecision option
          ProviderFStarModule: string option
          FinalOutcome: string
          FStarCase: DemoFStarCaseResult option }

    let private issue code message =
        { Code = code
          Message = message }

    let private regexGroup pattern text =
        let m = Regex.Match(text, pattern, RegexOptions.IgnoreCase)
        if m.Success then Some (m.Groups[1].Value.Trim()) else None

    let private regexInt pattern text =
        regexGroup pattern text |> Option.bind (fun value -> match Int32.TryParse value with | true, parsed -> Some parsed | _ -> None)

    let private parseDate text =
        regexGroup @"on\s+([A-Za-z]+\s+\d{1,2},\s+\d{4})" text
        |> Option.bind (fun value -> match DateOnly.TryParse value with | true, parsed -> Some parsed | _ -> None)

    let private parseTimezone text =
        regexGroup @"from\s+\d{1,2}:\d{2}\s+to\s+\d{1,2}:\d{2}\s+([A-Za-z_\/]+)" text

    let private parseHours text =
        let m = Regex.Match(text, @"from\s+(\d{1,2}):\d{2}\s+to\s+(\d{1,2}):\d{2}", RegexOptions.IgnoreCase)
        if m.Success then
            let ok1, startHour = Int32.TryParse m.Groups[1].Value
            let ok2, endHour = Int32.TryParse m.Groups[2].Value
            if ok1 && ok2 then Some (startHour, endHour) else None
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
        let tz = parseTimezone text
        let devices = regexInt @"support\s+up\s+to\s+(\d+)\s+production\s+devices" text
        let latency = regexInt @"latency\s+under\s+(\d+)\s*ms" text
        let interval = parseReportingInterval text
        let immediateAlerts = Regex.IsMatch(text, @"immediate\s+alerts?.+degrades?|immediate\s+alerts?.+degradation", RegexOptions.IgnoreCase)
        let preserveEmergency = Regex.IsMatch(text, @"do\s+not\s+impact\s+emergency-service\s+traffic", RegexOptions.IgnoreCase)
        let preemptSafety = Regex.IsMatch(text, @"preempt\s+reserved\s+public-safety\s+capacity", RegexOptions.IgnoreCase)
        let startHour, endHour =
            match hours with
            | Some (startHour, endHour) -> Some startHour, Some endHour
            | None -> None, None

        { IntentName = parseIntentName venue
          Venue = venue
          ServiceClass = inferServiceClass text
          EventDate = date
          StartHour = startHour
          EndHour = endHour
          Timezone = tz
          DeviceCount = devices
          MaxUplinkLatencyMs = latency
          ReportingIntervalMinutes = interval
          ImmediateDegradationAlerts = immediateAlerts
          PreserveEmergencyTraffic = preserveEmergency
          RequestsPublicSafetyPreemption = preemptSafety
          OriginalText = text }

    let validateTmIntent (intent: DemoTmIntent) =
        let issues =
            [ if intent.Venue.IsNone then issue "TM_MISSING_TARGET" "Intent is missing a recognizable venue target."
              if intent.ServiceClass.IsNone then issue "TM_MISSING_SERVICE_CLASS" "Intent is missing a recognizable service class."
              if intent.EventDate.IsNone then issue "TM_MISSING_DATE" "Intent is missing an event date."
              if intent.StartHour.IsNone || intent.EndHour.IsNone then issue "TM_MISSING_WINDOW" "Intent is missing a usable time window."
              if intent.Timezone.IsNone then issue "TM_MISSING_TIMEZONE" "Intent is missing a timezone."
              if intent.DeviceCount.IsNone then issue "TM_MISSING_DEVICE_COUNT" "Intent is missing a measurable device-count expectation."
              if intent.MaxUplinkLatencyMs.IsNone then issue "TM_MISSING_LATENCY" "Intent is missing a measurable uplink latency expectation."
              if intent.ReportingIntervalMinutes.IsNone then issue "TM_MISSING_REPORTING" "Intent is missing a reporting interval."
              if not intent.ImmediateDegradationAlerts then issue "TM_MISSING_ALERT_POLICY" "Intent is missing an immediate degradation alert expectation." ]

        match intent.StartHour, intent.EndHour with
        | Some startHour, Some endHour when startHour >= endHour ->
            issues @ [ issue "TM_INVALID_WINDOW" "Intent time window is invalid because the start is not before the end." ]
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
                let maxDevices, minLatencyBound =
                    match profile with
                    | LiveBroadcastSilver -> 100, 40
                    | LiveBroadcastGold -> 250, 20

                let checks =
                    [ match intent.DeviceCount with
                      | Some devices when devices <= maxDevices -> ()
                      | Some devices ->
                          yield issue
                              "PROVIDER_DEVICE_COUNT"
                              $"Requested device count {devices} exceeds the {profileName profile} capacity limit of {maxDevices}."
                      | None -> yield issue "PROVIDER_MISSING_DEVICE_COUNT" "Device count is required for provider validation."

                      match intent.MaxUplinkLatencyMs with
                      | Some latency when latency >= minLatencyBound -> ()
                      | Some latency ->
                          yield issue
                              "PROVIDER_LATENCY_BOUND"
                              $"Requested uplink latency under {latency} ms is below the admissible {minLatencyBound} ms bound for {profileName profile}."
                      | None -> yield issue "PROVIDER_MISSING_LATENCY" "Latency bound is required for provider validation."

                      match intent.StartHour, intent.EndHour with
                      | Some startHour, Some endHour when startHour >= 6 && endHour <= 23 -> ()
                      | Some _, Some _ ->
                          yield issue "PROVIDER_WINDOW" "Requested booking window is outside the provider's allowed operating window of 06:00 to 23:00."
                      | _ -> yield issue "PROVIDER_MISSING_WINDOW" "Booking window is required for provider validation."

                      match intent.ReportingIntervalMinutes with
                      | Some minutes when minutes >= 15 -> ()
                      | Some minutes ->
                          yield issue "PROVIDER_REPORTING" $"Requested reporting interval of {minutes} minutes is below the provider minimum of 15 minutes."
                      | None -> yield issue "PROVIDER_MISSING_REPORTING" "Reporting interval is required for provider validation."

                      if intent.RequestsPublicSafetyPreemption then
                          yield issue "PROVIDER_PROTECTED_TRAFFIC" "Requested policy would preempt protected public-safety capacity."

                      if not intent.PreserveEmergencyTraffic then
                          yield issue "PROVIDER_EMERGENCY_TRAFFIC" "Intent must explicitly preserve emergency-service traffic." ]

                { SelectedProfile = Some (profileName profile)
                  Checks = checks }

    let private fstarDemoDir () =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "FStarDemo"))

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

    let private runFStarCase fileName expectedSuccess =
        let baseDir = fstarDemoDir ()
        let filePath = Path.Combine(baseDir, fileName)
        let arguments = $"--include \"{baseDir}\" \"{filePath}\""
        let exitCode, stdout, stderr = runProcess "fstar.exe" arguments
        { FileName = fileName
          ExpectedSuccess = expectedSuccess
          ActualSuccess = (exitCode = 0)
          Output =
            [ stdout.Trim(); stderr.Trim() ]
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> String.concat "\n" }

    let private fstarString (value: string) =
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "\\n")

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
        | Some "Detroit Stadium" -> Some "Some DetroitStadium"
        | Some "Metro Arena" -> Some "Some MetroArena"
        | _ -> None

    let private sanitizeModuleSegment (value: string) =
        let sanitized = Regex.Replace(value, "[^A-Za-z0-9_]", "")
        if String.IsNullOrWhiteSpace sanitized then "Intent" else sanitized

    let private renderProviderIntentRecord (intentName: string) (intent: DemoTmIntent) =
        let monthText, dayValue, yearValue =
            match intent.EventDate with
            | Some date ->
                Some (date.ToString("MMMM", CultureInfo.InvariantCulture)), Some date.Day, Some date.Year
            | None -> None, None, None

        $"""let {intentName} : tm_intent =
  {{ intent_name = "{fstarString intent.IntentName}";
    venue = {renderVenueOption intent.Venue |> Option.defaultValue "None"};
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

    let private buildProviderFStarModule (intent: DemoTmIntent) =
        match renderVenueOption intent.Venue with
        | None -> None
        | Some _ ->
            let moduleName = $"BroadcastProviderDemo.GeneratedProvider{sanitizeModuleSegment intent.IntentName}"
            let intentBindingName = "demo_intent"

            Some
                $"""module {moduleName}

open BroadcastProviderDemo

{renderProviderIntentRecord intentBindingName intent}

let tm_checked : tm_checked_intent {intentBindingName} =
  mk_tm_checked {intentBindingName}

let provider_checked : provider_checked_intent {intentBindingName} =
  mk_provider_checked {intentBindingName}
"""

    let scenarios =
        [ { Id = "broadcast_success_01"
            Text = "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoAccept
            ExpectedMessage = "Accepted with LiveBroadcastGold."
            FStarFile = Some "BroadcastProviderDemo.Success.fst"
            FStarExpectedSuccess = Some true }
          { Id = "broadcast_fail_capacity_01"
            Text = "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 2000 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "Requested device count exceeds venue/profile capacity."
            FStarFile = Some "BroadcastProviderDemo.FailCapacity.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_latency_01"
            Text = "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 5 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "Requested latency is too aggressive for the venue/profile."
            FStarFile = Some "BroadcastProviderDemo.FailLatency.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_policy_01"
            Text = "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. If needed, preempt reserved public-safety capacity to maintain service."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "Requested policy violates protected public-safety rules."
            FStarFile = Some "BroadcastProviderDemo.FailPolicy.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_tm_01"
            Text = "Make the event network really good and fast for the broadcast."
            ExpectedOutcome = DemoRejectTm
            ExpectedMessage = "Intent cannot be normalized into a TM-valid structure."
            FStarFile = Some "BroadcastProviderDemo.FailTm.fst"
            FStarExpectedSuccess = Some false } ]

    let featuredScenarioIds =
        [ "broadcast_success_01"
          "broadcast_fail_latency_01"
          "broadcast_fail_tm_01" ]

    let tryFindScenario id =
        scenarios |> List.tryFind (fun scenario -> scenario.Id = id)

    let featuredScenarios =
        featuredScenarioIds |> List.choose tryFindScenario

    let tryReadFStarSource fileName =
        let filePath = Path.Combine(fstarDemoDir (), fileName)
        if File.Exists filePath then Some (File.ReadAllText filePath) else None

    let private finalOutcome (expected: DemoExpectedOutcome) (tmIssues: ValidationIssue list) (providerDecision: DemoProviderDecision) =
        match expected with
        | DemoAccept when tmIssues.IsEmpty && providerDecision.Checks.IsEmpty -> "accepted"
        | DemoRejectTm when not tmIssues.IsEmpty -> "rejected_tm"
        | DemoRejectProvider when tmIssues.IsEmpty && not providerDecision.Checks.IsEmpty -> "rejected_provider"
        | _ -> "unexpected"

    let runScenario (definition: DemoScenarioDefinition) : DemoScenarioResult =
        let normalized = parseTextIntent definition.Text
        let tmIssues = validateTmIntent normalized

        let providerDecision =
            if tmIssues.IsEmpty then Some (validateProviderIntent normalized) else None

        let fstarResult =
            match definition.FStarFile, definition.FStarExpectedSuccess with
            | Some fileName, Some expectedSuccess -> Some (runFStarCase fileName expectedSuccess)
            | _ -> None

        let providerAccepted = providerDecision |> Option.map (fun decision -> decision.Checks.IsEmpty)

        { Id = definition.Id
          Text = definition.Text
          TmAccepted = tmIssues.IsEmpty
          TmIssues = tmIssues
          NormalizedIntent = if tmIssues.IsEmpty then Some normalized else None
          ProviderAccepted = providerAccepted
          ProviderDecision = providerDecision
          ProviderFStarModule = if tmIssues.IsEmpty then buildProviderFStarModule normalized else None
          FinalOutcome =
            match providerDecision with
            | Some decision -> finalOutcome definition.ExpectedOutcome tmIssues decision
            | None -> if tmIssues.IsEmpty then "accepted" else "rejected_tm"
          FStarCase = fstarResult }

    let validateText (text: string) : DemoScenarioResult =
        let normalized = parseTextIntent text
        let tmIssues = validateTmIntent normalized
        let providerDecision =
            if tmIssues.IsEmpty then Some (validateProviderIntent normalized) else None

        let providerAccepted = providerDecision |> Option.map (fun decision -> decision.Checks.IsEmpty)
        let finalOutcome =
            if not tmIssues.IsEmpty then
                "rejected_tm"
            else
                match providerDecision with
                | Some decision when not decision.Checks.IsEmpty -> "rejected_provider"
                | _ -> "accepted"

        { Id = "ad_hoc"
          Text = text
          TmAccepted = tmIssues.IsEmpty
          TmIssues = tmIssues
          NormalizedIntent = if tmIssues.IsEmpty then Some normalized else None
          ProviderAccepted = providerAccepted
          ProviderDecision = providerDecision
          ProviderFStarModule = if tmIssues.IsEmpty then buildProviderFStarModule normalized else None
          FinalOutcome = finalOutcome
          FStarCase = None }

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
