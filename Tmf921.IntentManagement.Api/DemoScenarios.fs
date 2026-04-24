namespace Tmf921.IntentManagement.Api

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks

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
          ScenarioFamily: string option
          TargetName: string option
          TargetKind: string option
          Venue: string option
          ServiceClass: string option
          EventDate: DateOnly option
          StartHour: int option
          EndHour: int option
          Timezone: string option
          DeviceCount: int option
          PrimaryDeviceCount: int option
          AuxiliaryEndpointCount: int option
          MaxUplinkLatencyMs: int option
          MaxLatencyMs: int option
          ReportingIntervalMinutes: int option
          ImmediateDegradationAlerts: bool
          SafetyPolicyDeclared: bool
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
          ScenarioFamily: IntentAdmission.IntentScenarioFamily
          Title: string
          Kicker: string
          Text: string
          ExpectedOutcome: DemoExpectedOutcome
          ExpectedMessage: string
          ExpectedJsonAccepted: bool
          ExpectedFailedWitness: string option
          Story: string
          ReferenceIntent: IntentAdmission.RestrictedIntent option
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

    let private trimToOption (value: string option) =
        value
        |> Option.bind (fun text ->
            let trimmed = text.Trim()
            if String.IsNullOrWhiteSpace trimmed then None else Some trimmed)

    let private normalizeToken (value: string) =
        value.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "")

    let private containsText (needle: string) (value: string) =
        value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0

    let private splitOnce (separator: string) (value: string) =
        let index = value.IndexOf(separator, StringComparison.Ordinal)
        if index < 0 then None else Some(value.Substring(0, index), value.Substring(index + separator.Length))

    let private tryParseIntText (value: string option) =
        value
        |> trimToOption
        |> Option.bind (fun text ->
            match Int32.TryParse text with
            | true, parsed -> Some parsed
            | _ -> None)

    let private tryParseDateText (value: string) =
        match DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture) with
        | true, parsed -> Some parsed
        | _ -> None

    let private tryParseHourText (value: string) =
        let trimmed = value.Trim()
        let hourText =
            match splitOnce ":" trimmed with
            | Some(hour, _) -> hour
            | None -> trimmed

        match Int32.TryParse hourText with
        | true, parsed when parsed >= 0 && parsed <= 23 -> Some parsed
        | _ -> None

    let private expectationTexts (expectation: CanonicalExpectation) =
        [ Some expectation.Kind
          Some expectation.Subject
          expectation.Description
          expectation.Condition |> Option.map (fun condition -> condition.Kind)
          expectation.Condition |> Option.bind (fun condition -> condition.Subject)
          expectation.Condition |> Option.bind (fun condition -> condition.Operator)
          expectation.Condition |> Option.bind (fun condition -> condition.Value)
          expectation.Quantity |> Option.map (fun quantity -> quantity.Value)
          expectation.Quantity |> Option.bind (fun quantity -> quantity.Unit)
          expectation.FunctionApplication |> Option.map (fun application -> application.Name)
          expectation.FunctionApplication |> Option.map (fun application -> String.concat " " application.Arguments) ]
        |> List.choose trimToOption

    let private canonicalTexts (canonical: CanonicalIntentIr) =
        [ canonical.Description
          canonical.Context
          canonical.Priority
          yield!
            canonical.Expectations
            |> List.collect expectationTexts
            |> List.map Some ]
        |> List.choose trimToOption

    let private expectationMatches (tokens: string list) (expectation: CanonicalExpectation) =
        let normalizedTexts =
            expectationTexts expectation |> List.map normalizeToken

        tokens
        |> List.exists (fun token ->
            normalizedTexts
            |> List.exists (fun text -> text = token || text.Contains(token, StringComparison.Ordinal)))

    let private tryFindExpectation tokens (canonical: CanonicalIntentIr) =
        canonical.Expectations |> List.tryFind (expectationMatches tokens)

    let private parseDateTimeParts (parts: string list) =
        let cleanToken (value: string) =
            value.Trim().TrimEnd('.', ';', ',')

        let parseTimezone remainder =
            let cleaned = remainder |> List.map cleanToken |> List.filter (String.IsNullOrWhiteSpace >> not)

            match cleaned with
            | [] -> None
            | first :: _ when first.Contains("/") || first.Contains("_") -> Some first
            | first :: _ -> Some first

        match parts with
        | dateText :: timeText :: remainder when tryParseDateText dateText |> Option.isSome ->
            tryParseDateText dateText, tryParseHourText timeText, parseTimezone remainder
        | timeText :: remainder ->
            None, tryParseHourText timeText, parseTimezone remainder
        | _ -> None, None, None

    let private tryParseWindowValue (value: string) =
        match splitOnce " to " (value.Trim()) with
        | None -> None
        | Some(startText, endText) ->
            let startParts =
                startText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList

            let endParts =
                endText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList

            let startDate, startHour, _ = parseDateTimeParts startParts
            let endDate, endHour, timezone = parseDateTimeParts endParts

            Some(startDate |> Option.orElse endDate, startHour, endHour, timezone)

    let private tryExtractWindowFromText (text: string) =
        let lower = text.ToLowerInvariant()
        let onIndex = lower.IndexOf(" on ", StringComparison.Ordinal)
        let fromIndex = lower.IndexOf(" from ", StringComparison.Ordinal)
        let toIndex =
            if fromIndex >= 0 then
                lower.IndexOf(" to ", fromIndex + 6, StringComparison.Ordinal)
            else
                -1

        if onIndex < 0 || fromIndex <= onIndex || toIndex <= fromIndex then
            None
        else
            let dateText =
                text.Substring(onIndex + 4, fromIndex - (onIndex + 4)).Trim().TrimEnd('.')

            let startText =
                text.Substring(fromIndex + 6, toIndex - (fromIndex + 6)).Trim()

            let endText =
                text.Substring(toIndex + 4).Trim().TrimEnd('.')

            let endParts =
                endText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList

            let _, endHour, timezone = parseDateTimeParts endParts

            Some(tryParseDateText dateText, tryParseHourText startText, endHour, timezone)

    let private tryExtractWindow (canonical: CanonicalIntentIr) =
        let fromCondition =
            canonical.Expectations
            |> List.tryPick (fun expectation ->
                expectation.Condition
                |> Option.bind (fun condition ->
                    let conditionKind = normalizeToken condition.Kind
                    let conditionOperator = condition.Operator |> Option.map normalizeToken |> Option.defaultValue ""

                    if conditionKind = "timewindow" || conditionOperator = "between" then
                        condition.Value |> Option.bind tryParseWindowValue
                    else
                        None))

        fromCondition
        |> Option.orElseWith (fun () ->
            canonicalTexts canonical
            |> List.tryPick tryExtractWindowFromText)

    let private parseQuantityValue (expectation: CanonicalExpectation) =
        expectation.Quantity
        |> Option.bind (fun quantity -> tryParseIntText (Some quantity.Value))

    let private parseReportingMinutes (expectation: CanonicalExpectation) =
        let fromQuantity =
            expectation.Quantity
            |> Option.bind (fun quantity ->
                let quantityValue = tryParseIntText (Some quantity.Value)
                let quantityUnit = quantity.Unit |> Option.map normalizeToken |> Option.defaultValue ""

                match quantityValue with
                | Some value when quantityUnit.Contains("hour", StringComparison.Ordinal) -> Some(value * 60)
                | Some value when quantityUnit.Contains("minute", StringComparison.Ordinal) -> Some value
                | Some value when quantityUnit.Contains("min", StringComparison.Ordinal) -> Some value
                | _ -> None)

        let fromCondition =
            expectation.Condition
            |> Option.bind (fun condition ->
                match condition.Value |> trimToOption with
                | Some value when String.Equals(value, "hourly", StringComparison.OrdinalIgnoreCase) ->
                    Some 60
                | Some value when String.Equals(value, "daily", StringComparison.OrdinalIgnoreCase) ->
                    Some 1440
                | Some value when containsText "hour" value ->
                    value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.tryHead
                    |> tryParseIntText
                    |> Option.map (fun hours -> hours * 60)
                | Some value when containsText "minute" value ->
                    value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.tryHead
                    |> tryParseIntText
                | _ -> None)

        fromQuantity |> Option.orElse fromCondition

    let private collectPolicyTexts (canonical: CanonicalIntentIr) =
        canonical.Expectations
        |> List.collect expectationTexts

    let private emptyDemoIntent text =
        { IntentName = "LiveBroadcastIntent"
          ScenarioFamily = None
          TargetName = None
          TargetKind = None
          Venue = None
          ServiceClass = None
          EventDate = None
          StartHour = None
          EndHour = None
          Timezone = None
          DeviceCount = None
          PrimaryDeviceCount = None
          AuxiliaryEndpointCount = None
          MaxUplinkLatencyMs = None
          MaxLatencyMs = None
          ReportingIntervalMinutes = None
          ImmediateDegradationAlerts = false
          SafetyPolicyDeclared = false
          PreserveEmergencyTraffic = false
          RequestsPublicSafetyPreemption = false
          OriginalText = text }

    let private canonicalToDemoIntent (text: string) (canonical: CanonicalIntentIr) =
        let venue =
            canonical.Targets
            |> List.tryPick (fun target ->
                target.Name
                |> trimToOption
                |> Option.orElseWith (fun () ->
                    if containsText "target-" target.Id then None else Some target.Id))

        let eventDate, startHour, endHour, timezone =
            tryExtractWindow canonical
            |> Option.defaultValue(None, None, None, None)

        let capacityExpectation = tryFindExpectation [ "capacity"; "devices"; "productiondevices" ] canonical
        let latencyExpectation = tryFindExpectation [ "latency"; "uplinklatency"; "uplink" ] canonical
        let reportingExpectation = tryFindExpectation [ "reporting"; "complianceupdates"; "compliance" ] canonical
        let alertingExpectation = tryFindExpectation [ "alerting"; "servicequalitydegradation"; "servicequality" ] canonical

        let allTexts = canonicalTexts canonical
        let allJoinedText = String.concat " " allTexts
        let priorityText = canonical.Priority |> Option.defaultValue ""

        let serviceClass =
            if containsText "broadcast" allJoinedText
               && containsText "5g" allJoinedText
               && (containsText "premium" allJoinedText || containsText "premium" priorityText)
            then
                Some "premium-5g-broadcast"
            else
                None

        let preserveEmergencyTraffic =
            collectPolicyTexts canonical
            |> List.exists (fun value ->
                containsText "emergency-service traffic" value
                || containsText "emergency service traffic" value)
            && not (
                collectPolicyTexts canonical
                |> List.exists (fun value -> containsText "preempt" value && containsText "public-safety" value)
            )
            ||
            canonical.Expectations
            |> List.exists (fun expectation ->
                let operatorText =
                    expectation.Condition
                    |> Option.bind (fun condition -> condition.Operator)
                    |> Option.map normalizeToken
                    |> Option.defaultValue ""

                (containsText "emergency-service traffic" (String.concat " " (expectationTexts expectation))
                 || containsText "emergency service traffic" (String.concat " " (expectationTexts expectation)))
                && (operatorText = "noimpact" || operatorText = "notimpact" || containsText "do not impact" (String.concat " " (expectationTexts expectation))))

        let requestsPublicSafetyPreemption =
            collectPolicyTexts canonical
            |> List.exists (fun value ->
                containsText "public-safety" value
                || containsText "public safety" value
                || containsText "preempt" value)

        { IntentName = canonical.IntentName
          ScenarioFamily = Some "LiveBroadcast"
          TargetName = venue
          TargetKind = Some "VenueTarget"
          Venue = venue
          ServiceClass = serviceClass
          EventDate = eventDate
          StartHour = startHour
          EndHour = endHour
          Timezone = timezone |> trimToOption
          DeviceCount = capacityExpectation |> Option.bind parseQuantityValue
          PrimaryDeviceCount = capacityExpectation |> Option.bind parseQuantityValue
          AuxiliaryEndpointCount = None
          MaxUplinkLatencyMs = latencyExpectation |> Option.bind parseQuantityValue
          MaxLatencyMs = latencyExpectation |> Option.bind parseQuantityValue
          ReportingIntervalMinutes = reportingExpectation |> Option.bind parseReportingMinutes
          ImmediateDegradationAlerts =
            match alertingExpectation with
            | Some expectation ->
                expectationTexts expectation
                |> List.exists (containsText "immediate")
                || true
            | None -> false
          SafetyPolicyDeclared = preserveEmergencyTraffic || requestsPublicSafetyPreemption
          PreserveEmergencyTraffic = preserveEmergencyTraffic
          RequestsPublicSafetyPreemption = requestsPublicSafetyPreemption
          OriginalText = text }

    let private optionOr first second =
        match first with
        | Some _ -> first
        | None -> second

    let private demoIntentFromRestricted (text: string) (intent: IntentAdmission.RestrictedIntent) =
        let targetName = intent.TargetName |> trimToOption
        let targetKind = intent.TargetKind |> Option.map IntentAdmission.targetKindName
        let primaryDeviceCount = intent.PrimaryDeviceCount
        let maxLatencyMs = intent.MaxLatencyMs

        { IntentName = intent.IntentName
          ScenarioFamily = Some(IntentAdmission.familyName intent.ScenarioFamily)
          TargetName = targetName
          TargetKind = targetKind
          Venue =
            match intent.ScenarioFamily with
            | IntentAdmission.LiveBroadcast -> targetName
            | IntentAdmission.CriticalService -> None
          ServiceClass = intent.ServiceClass |> trimToOption
          EventDate = intent.EventDate
          StartHour = intent.StartHour
          EndHour = intent.EndHour
          Timezone = intent.Timezone |> trimToOption
          DeviceCount =
            match intent.ScenarioFamily with
            | IntentAdmission.LiveBroadcast -> primaryDeviceCount
            | IntentAdmission.CriticalService -> None
          PrimaryDeviceCount = primaryDeviceCount
          AuxiliaryEndpointCount = intent.AuxiliaryEndpointCount
          MaxUplinkLatencyMs =
            match intent.ScenarioFamily with
            | IntentAdmission.LiveBroadcast -> maxLatencyMs
            | IntentAdmission.CriticalService -> None
          MaxLatencyMs = maxLatencyMs
          ReportingIntervalMinutes = intent.ReportingIntervalMinutes
          ImmediateDegradationAlerts = intent.ImmediateDegradationAlerts
          SafetyPolicyDeclared = intent.SafetyPolicyDeclared
          PreserveEmergencyTraffic = intent.PreserveEmergencyTraffic
          RequestsPublicSafetyPreemption = intent.RequestPublicSafetyPreemption
          OriginalText = text }

    let private demoIntentFromOperational (text: string) (record: OperationalIntentRecord) =
        IntentAdmission.tryFromOperationalIntentRecord record
        |> Result.toOption
        |> Option.map (demoIntentFromRestricted text)

    let private restrictedFromDemoIntent (intent: DemoTmIntent) : IntentAdmission.RestrictedIntent =
        let family =
            match intent.ScenarioFamily with
            | Some "CriticalService" -> IntentAdmission.CriticalService
            | _ -> IntentAdmission.LiveBroadcast

        let targetName = intent.TargetName |> optionOr intent.Venue
        let targetKind =
            match intent.TargetKind with
            | Some "FacilityTarget" -> Some IntentAdmission.FacilityTarget
            | Some "VenueTarget" -> Some IntentAdmission.VenueTarget
            | _ ->
                match family with
                | IntentAdmission.LiveBroadcast when targetName.IsSome -> Some IntentAdmission.VenueTarget
                | IntentAdmission.CriticalService when targetName.IsSome -> Some IntentAdmission.FacilityTarget
                | _ -> None

        { IntentName = intent.IntentName
          ScenarioFamily = family
          TargetName = targetName
          TargetKind = targetKind
          ServiceClass = intent.ServiceClass
          EventDate = intent.EventDate
          StartHour = intent.StartHour
          EndHour = intent.EndHour
          Timezone = intent.Timezone
          PrimaryDeviceCount = intent.PrimaryDeviceCount |> optionOr intent.DeviceCount
          AuxiliaryEndpointCount = intent.AuxiliaryEndpointCount
          MaxLatencyMs = intent.MaxLatencyMs |> optionOr intent.MaxUplinkLatencyMs
          ReportingIntervalMinutes = intent.ReportingIntervalMinutes
          ImmediateDegradationAlerts = intent.ImmediateDegradationAlerts
          SafetyPolicyDeclared = intent.SafetyPolicyDeclared
          PreserveEmergencyTraffic = intent.PreserveEmergencyTraffic
          RequestPublicSafetyPreemption = intent.RequestsPublicSafetyPreemption
          SourceText = Some intent.OriginalText }

    let private readArtifactText (reference: ArtifactReference option) =
        reference
        |> Option.bind (fun artifact ->
            if File.Exists artifact.Path then
                Some(File.ReadAllText(artifact.Path, Encoding.UTF8))
            else
                None)

    let private excerptText (text: string) =
        if String.IsNullOrWhiteSpace text then
            None
        else if text.Length <= 2000 then
            Some text
        else
            Some(text.Substring(0, 2000) + "\n... output truncated ...")

    let private checkerExcerptFromPath path =
        if String.IsNullOrWhiteSpace path || not (File.Exists path) then
            None
        else
            let document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8))
            let root = document.RootElement

            let property (name: string) =
                let mutable element = Unchecked.defaultof<JsonElement>
                if root.TryGetProperty(name, &element) then
                    element.GetString() |> Option.ofObj |> trimToOption
                else
                    None

            [ property "stderr"; property "stdout" ]
            |> List.choose id
            |> List.tryFind (String.IsNullOrWhiteSpace >> not)
            |> Option.map excerptText
            |> Option.defaultValue None

    let private failedWitnessIssue witness =
        match witness with
        | "measurable_intent" ->
            Some(issue "TM_MEASURABLE_INTENT" "The request is still missing measurable TM intent fields.")
        | "quantity_checked_intent" ->
            Some(issue "TM_QUANTITY_INTENT" "The request contains non-positive or missing measured quantities.")
        | "window_checked_intent" ->
            Some(issue "PROVIDER_WINDOW" "Provider refinement rejected the requested service window.")
        | "profiled_intent" ->
            Some(issue "PROVIDER_PROFILE" "The normalized target does not resolve to a supported provider profile.")
        | "capacity_checked_intent" ->
            Some(issue "PROVIDER_CAPACITY" "The request exceeds provider capacity, reporting, or operating-window constraints.")
        | "latency_checked_intent" ->
            Some(issue "PROVIDER_LATENCY" "The request violates the resolved provider profile's latency floor.")
        | "policy_checked_intent" ->
            Some(issue "PROVIDER_POLICY" "The request violates alerting, safety, or protected-traffic policy constraints.")
        | "candidate_module_compile" ->
            Some(issue "FSTAR_CANDIDATE" "The generated candidate module did not compile in F*.")
        | _ -> None

    let validateTmIntent (intent: DemoTmIntent) =
        let restricted = restrictedFromDemoIntent intent

        let issues =
            [ if restricted.TargetName.IsNone then
                  issue "TM_MISSING_TARGET" "Intent is missing a recognizable target."
              if restricted.TargetKind.IsNone then
                  issue "TM_MISSING_TARGET_KIND" "Intent is missing a recognized target kind."
              if restricted.ServiceClass.IsNone then
                  issue "TM_MISSING_SERVICE_CLASS" "Intent is missing a recognizable service class."
              if restricted.EventDate.IsNone then
                  issue "TM_MISSING_DATE" "Intent is missing an event date."
              if restricted.StartHour.IsNone || restricted.EndHour.IsNone then
                  issue "TM_MISSING_WINDOW" "Intent is missing a usable time window."
              if restricted.Timezone.IsNone then
                  issue "TM_MISSING_TIMEZONE" "Intent is missing a timezone."
              if restricted.PrimaryDeviceCount.IsNone then
                  issue "TM_MISSING_DEVICE_COUNT" "Intent is missing a measurable primary device-count expectation."
              if restricted.ScenarioFamily = IntentAdmission.CriticalService && restricted.AuxiliaryEndpointCount.IsNone then
                  issue "TM_MISSING_AUXILIARY_COUNT" "Intent is missing a measurable auxiliary endpoint-count expectation."
              if restricted.MaxLatencyMs.IsNone then
                  issue "TM_MISSING_LATENCY" "Intent is missing a measurable latency expectation."
              if restricted.ReportingIntervalMinutes.IsNone then
                  issue "TM_MISSING_REPORTING" "Intent is missing a reporting interval."
              match restricted.PrimaryDeviceCount with
              | Some value when value <= 0 ->
                  issue "TM_NON_POSITIVE_DEVICE_COUNT" "Intent device counts must be positive quantities."
              | _ -> ()
              match restricted.AuxiliaryEndpointCount with
              | Some value when value <= 0 ->
                  issue "TM_NON_POSITIVE_AUXILIARY_COUNT" "Intent auxiliary endpoint counts must be positive quantities."
              | _ -> ()
              match restricted.MaxLatencyMs with
              | Some value when value <= 0 ->
                  issue "TM_NON_POSITIVE_LATENCY" "Intent latency bounds must be positive quantities."
              | _ -> ()
              match restricted.ReportingIntervalMinutes with
              | Some value when value <= 0 ->
                  issue "TM_NON_POSITIVE_REPORTING" "Intent reporting intervals must be positive quantities."
              | _ -> () ]

        issues |> List.distinctBy (fun value -> value.Code, value.Message)

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
        let restricted = restrictedFromDemoIntent intent

        { SelectedProfile = IntentAdmission.resolveProviderProfileName restricted
          Checks =
            [ yield! IntentAdmission.providerFailedWitness restricted |> Option.bind failedWitnessIssue |> Option.toList
              if IntentAdmission.tmFailedWitness restricted |> Option.isNone && IntentAdmission.resolveProviderProfileName restricted |> Option.isNone then
                  yield issue "PROVIDER_PROFILE" "The normalized target does not resolve to a supported provider profile." ]
            |> List.distinctBy (fun value -> value.Code, value.Message) }

    let private tmFailedWitness (intent: DemoTmIntent) =
        restrictedFromDemoIntent intent |> IntentAdmission.tmFailedWitness

    let private providerFailedWitness (intent: DemoTmIntent) =
        restrictedFromDemoIntent intent |> IntentAdmission.providerFailedWitness

    let private demoProjectDir () =
        Path.Combine(repoRoot (), "Tmf921.IntentManagement.Api")

    let private demoSchemaPath family =
        let fileName =
            match family with
            | Some "CriticalService" -> "CriticalServiceIntent.schema.json"
            | _ -> "BroadcastIntent.schema.json"

        Path.Combine(demoProjectDir (), "DemoSchemas", fileName)

    let private fstarDemoDir () =
        Path.Combine(demoProjectDir (), "FStarDemo")

    let private fstarLibraryDir () =
        Path.Combine(demoProjectDir (), "FStar")

    let private fstarGeneratedDemoDir () =
        Path.Combine(fstarDemoDir (), "generated")

    let private jsonBaselineSchemas =
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Json.Schema.JsonSchema>>()

    let private jsonBaselineSchema family =
        let key =
            match family with
            | Some "CriticalService" -> "CriticalService"
            | _ -> "Broadcast"

        jsonBaselineSchemas.GetOrAdd(
            key,
            fun _ ->
                lazy
                    let schemaText = File.ReadAllText(demoSchemaPath family)
                    Json.Schema.JsonSchema.FromText(schemaText)
        )

    let private buildJsonBaselineInstance (intent: DemoTmIntent) =
        let eventDate =
            intent.EventDate
            |> Option.map (fun value -> value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))

        match intent.ScenarioFamily with
        | Some "CriticalService" ->
            JsonSerializer.SerializeToElement(
                {| intentName = intent.IntentName
                   facility = intent.TargetName
                   serviceClass = intent.ServiceClass
                   eventDate = eventDate
                   startHour = intent.StartHour
                   endHour = intent.EndHour
                   timezone = intent.Timezone
                   criticalDeviceCount = intent.PrimaryDeviceCount |> optionOr intent.DeviceCount
                   auxiliaryEndpointCount = intent.AuxiliaryEndpointCount
                   maxEndToEndLatencyMs = intent.MaxLatencyMs |> optionOr intent.MaxUplinkLatencyMs
                   reportingIntervalMinutes = intent.ReportingIntervalMinutes
                   immediateDegradationAlerts = intent.ImmediateDegradationAlerts
                   safetyPolicyDeclared = intent.SafetyPolicyDeclared
                   preserveEmergencyTraffic = intent.PreserveEmergencyTraffic
                   requestsPublicSafetyPreemption = intent.RequestsPublicSafetyPreemption |},
                serializerOptions)
        | _ ->
            JsonSerializer.SerializeToElement(
                {| intentName = intent.IntentName
                   venue = intent.TargetName |> optionOr intent.Venue
                   serviceClass = intent.ServiceClass
                   eventDate = eventDate
                   startHour = intent.StartHour
                   endHour = intent.EndHour
                   timezone = intent.Timezone
                   deviceCount = intent.PrimaryDeviceCount |> optionOr intent.DeviceCount
                   maxUplinkLatencyMs = intent.MaxLatencyMs |> optionOr intent.MaxUplinkLatencyMs
                   reportingIntervalMinutes = intent.ReportingIntervalMinutes
                   immediateDegradationAlerts = intent.ImmediateDegradationAlerts
                   preserveEmergencyTraffic = intent.PreserveEmergencyTraffic
                   requestsPublicSafetyPreemption = intent.RequestsPublicSafetyPreemption |},
                serializerOptions)

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

    let private validateJsonBaseline (intent: DemoTmIntent) =
        let options = Json.Schema.EvaluationOptions()
        options.OutputFormat <- Json.Schema.OutputFormat.List
        options.RequireFormatValidation <- true

        let result =
            jsonBaselineSchema (intent.ScenarioFamily)
            |> fun schema -> schema.Value.Evaluate(buildJsonBaselineInstance intent, options)
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
        let libraryDir = fstarLibraryDir ()
        let filePath = Path.Combine(baseDir, fileName)
        let arguments = $"--include \"{baseDir}\" --include \"{libraryDir}\" \"{filePath}\""
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
        let sanitized =
            value
            |> Seq.filter (fun ch -> Char.IsLetterOrDigit ch || ch = '_')
            |> Seq.toArray
            |> String

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

let quantity_checked : quantity_checked_intent {intentBindingName} =
  mk_quantity_checked {intentBindingName}

let window_checked : window_checked_intent {intentBindingName} =
  mk_window_checked {intentBindingName}

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
        let generatedDir = fstarGeneratedDemoDir ()
        let moduleName = $"BroadcastProviderDemo.Generated{sanitizeModuleSegment moduleKey}"
        let filePath = Path.Combine(generatedDir, $"{moduleName}.fst")
        let moduleText = buildProviderFStarModule moduleName intent

        Directory.CreateDirectory(generatedDir) |> ignore
        File.WriteAllText(filePath, moduleText, Encoding.UTF8)

        let arguments = $"--include \"{baseDir}\" --include \"{generatedDir}\" \"{filePath}\""
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
              | Some "candidate_module_compile" ->
                  stage "Candidate module compile" "candidate_module_compile" "failed" "The generated candidate module did not type-check in F*."
                  stage "TR292 measurability" "measurable_intent" "skipped" "Common-core witnesses cannot be composed until the candidate module compiles."
                  stage "Quantity sanity" "quantity_checked_intent" "skipped" "Quantity refinement depends on a compiled candidate module."
              | Some "measurable_intent" ->
                  stage "Candidate module compile" "candidate_module_compile" "passed" "The generated candidate module type-checks in F*."
                  stage "TR292 measurability" "measurable_intent" "failed" "Required common-core fields are still missing."
                  stage "Quantity sanity" "quantity_checked_intent" "skipped" "Quantity refinement depends on measurable common-core fields."
              | Some "quantity_checked_intent" ->
                  stage "Candidate module compile" "candidate_module_compile" "passed" "The generated candidate module type-checks in F*."
                  stage "TR292 measurability" "measurable_intent" "passed" "All common-core measurable fields are present."
                  stage "Quantity sanity" "quantity_checked_intent" "failed" "One or more measured quantities are missing or non-positive."
              | _ ->
                  stage "Candidate module compile" "candidate_module_compile" "passed" "The generated candidate module type-checks in F*."
                  stage "TR292 measurability" "measurable_intent" "passed" "All common-core measurable fields are present."
                  stage "Quantity sanity" "quantity_checked_intent" "passed" "The intent satisfies the TR292/common-core quantity checks." ]

        let providerStages =
            if providerSkipped then
                [ stage "Window sanity" "window_checked_intent" "skipped" "Provider refinement is skipped until the TR292/common-core witness exists."
                  stage "Profile resolution" "profiled_intent" "skipped" "Provider reasoning is skipped until the TR292/common-core witness exists."
                  stage "Capacity and operating envelope" "capacity_checked_intent" "skipped" "Provider constraints depend on a common-core witness."
                  stage "Latency floor" "latency_checked_intent" "skipped" "Latency refinement depends on earlier provider stages."
                  stage "Safety and traffic policy" "policy_checked_intent" "skipped" "Policy refinement depends on earlier provider stages."
                  stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission is not attempted."
                  stage "Admission token" "admission_token" "skipped" "No downstream token can be issued." ]
            else
                [ match providerFailure with
                  | Some "window_checked_intent" ->
                      stage "Window sanity" "window_checked_intent" "failed" "The requested service window is not strictly increasing."
                      stage "Profile resolution" "profiled_intent" "skipped" "Profile resolution depends on a valid service window."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "skipped" "Capacity checking depends on the provider window stage."
                      stage "Latency floor" "latency_checked_intent" "skipped" "Latency refinement depends on earlier provider stages."
                      stage "Safety and traffic policy" "policy_checked_intent" "skipped" "Policy refinement depends on earlier provider stages."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at window refinement."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | Some "profiled_intent" ->
                      stage "Window sanity" "window_checked_intent" "passed" "The requested service window is semantically valid."
                      stage "Profile resolution" "profiled_intent" "failed" "The normalized venue does not map to a supported provider profile."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "skipped" "Capacity checking depends on a resolved provider profile."
                      stage "Latency floor" "latency_checked_intent" "skipped" "Latency refinement depends on earlier provider stages."
                      stage "Safety and traffic policy" "policy_checked_intent" "skipped" "Policy refinement depends on earlier provider stages."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at profile resolution."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | Some "capacity_checked_intent" ->
                      stage "Window sanity" "window_checked_intent" "passed" "The requested service window is semantically valid."
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "failed" "Capacity, reporting, or provider operating-window constraints were violated."
                      stage "Latency floor" "latency_checked_intent" "skipped" "Latency refinement depends on the capacity stage."
                      stage "Safety and traffic policy" "policy_checked_intent" "skipped" "Policy refinement depends on earlier provider stages."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at the capacity stage."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | Some "latency_checked_intent" ->
                      stage "Window sanity" "window_checked_intent" "passed" "The requested service window is semantically valid."
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "failed" "The request undercuts the profile-specific latency floor."
                      stage "Safety and traffic policy" "policy_checked_intent" "skipped" "Policy refinement depends on the latency stage."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at the latency stage."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | Some "policy_checked_intent" ->
                      stage "Window sanity" "window_checked_intent" "passed" "The requested service window is semantically valid."
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "passed" "The request satisfies the profile-specific latency floor."
                      stage "Safety and traffic policy" "policy_checked_intent" "failed" "Alerting, safety-declaration, or protected-traffic constraints were violated."
                      stage "Provider admission" "provider_checked_intent" "skipped" "Provider admission stops at the policy stage."
                      stage "Admission token" "admission_token" "skipped" "No downstream token can be issued."
                  | _ when providerAccepted ->
                      stage "Window sanity" "window_checked_intent" "passed" "The requested service window is semantically valid."
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "passed" "The request satisfies the profile-specific latency floor."
                      stage "Safety and traffic policy" "policy_checked_intent" "passed" "The request satisfies alerting, safety, and protected-traffic policy."
                      stage "Provider admission" "provider_checked_intent" "passed" "A provider-level witness has been constructed."
                      stage "Admission token" "admission_token" "passed" "A downstream typed admission token is now constructible."
                  | _ ->
                      let providerSummary =
                          if providerExpectedAccepted then
                              "F* rejected a request that the explainer path expected to admit."
                          else
                              "F* rejected the request at provider admission."

                      stage "Window sanity" "window_checked_intent" "passed" "The requested service window is semantically valid."
                      stage "Profile resolution" "profiled_intent" "passed" "A concrete provider profile has been resolved."
                      stage "Capacity and operating envelope" "capacity_checked_intent" "passed" "Capacity, reporting, and operating-window checks passed."
                      stage "Latency floor" "latency_checked_intent" "passed" "The request satisfies the profile-specific latency floor."
                      stage "Safety and traffic policy" "policy_checked_intent" "passed" "The request satisfies alerting, safety, and protected-traffic policy."
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
                "The JSON shape passes, but the TR292/common-core witness cannot be composed because required common-core semantics are still unmet."
            else
                match dependentProvider.Accepted with
                | Some true ->
                    "The request survives shape checks and composes all the way to a typed admission token."
                | Some false ->
                    let failedWitness =
                        dependentProvider.FailedWitness |> Option.defaultValue "provider_checked_intent"

                    $"The JSON shape passes, but the provider witness chain breaks at {failedWitness}, which exposes domain semantics beyond schema validation."
                | None ->
                    "Provider reasoning stays dormant until the TR292/common-core witness exists."

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

    let private adHocDefinition text =
        { Id = "ad_hoc"
          ScenarioFamily = IntentAdmission.LiveBroadcast
          Title = "Ad Hoc Statement"
          Kicker = "Interactive"
          Text = text
          ExpectedOutcome = DemoAccept
          ExpectedMessage = "Ad hoc validation request."
          ExpectedJsonAccepted = false
          ExpectedFailedWitness = None
          Story = ""
          ReferenceIntent = None
          FStarFile = None
          FStarExpectedSuccess = None }

    let private issuesFromDiagnostics (diagnostics: ProcessingDiagnostic list) =
        diagnostics
        |> List.map (fun diagnostic ->
            let message =
                match diagnostic.Details with
                | Some details when not (String.IsNullOrWhiteSpace details) ->
                    $"{diagnostic.Message} Details: {details}"
                | _ -> diagnostic.Message

            issue diagnostic.Code message)

    let private distinctIssues (issues: ValidationIssue list) =
        issues |> List.distinctBy (fun value -> value.Code, value.Message)

    let private generationResultFromReferenceIntent (definition: DemoScenarioDefinition) intent =
        let moduleName = $"DemoIntent_{sanitizeModuleSegment definition.Id}"
        let moduleText = IntentAdmission.buildCandidateModule moduleName intent
        let envelope =
            { Status = "parsed"
              ModuleText = Some moduleText
              Issues = [] }
        let attempt =
            { Attempt = 1
              Source = "scenario_reference"
              Outcome = "parsed"
              ResponseId = None
              FinishReason = None
              Issues = [] }

        { Envelope = Some envelope
          Metadata =
            { Provider = Some "scenario_reference"
              Model = Some "checked-in-reference-intent"
              PromptVersion = Some "demo-reference"
              SelectedOutcome = Some "parsed"
              UsedFixture = true
              FixtureId = Some definition.Id
              Attempts = [ attempt ] }
          PromptText = None
          RawResponseText = Some moduleText
          Diagnostics = [] }

    let private generatorForDefinition
        (rawIntentGenerator: IRawIntentGenerator)
        (definitionOption: DemoScenarioDefinition option) =
        match definitionOption with
        | Some definition ->
            match definition.ReferenceIntent with
            | Some intent ->
                { new IRawIntentGenerator with
                    member _.GenerateIntentModuleAsync(_, _, _) =
                        Task.FromResult(generationResultFromReferenceIntent definition intent) }
            | None -> rawIntentGenerator
        | None -> rawIntentGenerator

    let private successfulExpectationChecks =
        { JsonBaselineMatches = true
          FailedWitnessMatches = true
          DependentAgreement = true
          Mismatches = [] }

    let private evaluateProcessedScenario
        (definitionOption: DemoScenarioDefinition option)
        (moduleKey: string)
        (text: string)
        (record: IntentProcessingRecord) =
        let definition =
            definitionOption |> Option.defaultWith (fun () -> adHocDefinition text)

        let tmStageWitnesses =
            set [ "candidate_module_compile"; "measurable_intent"; "quantity_checked_intent" ]

        let normalizedIntent =
            record.OperationalIntent
            |> Option.bind (demoIntentFromOperational text)
            |> Option.orElseWith (fun () -> record.CanonicalIntent |> Option.map (canonicalToDemoIntent text))

        let referenceIntent =
            definition.ReferenceIntent |> Option.map (demoIntentFromRestricted definition.Text)

        let workingIntent =
            normalizedIntent
            |> Option.orElse referenceIntent
            |> Option.defaultValue (emptyDemoIntent text)

        let jsonBaseline = validateJsonBaseline workingIntent

        let tmFailedWitness =
            match record.FirstFailedWitness with
            | Some witness when tmStageWitnesses.Contains witness -> Some witness
            | _ when record.TmWitnessStatus = Some "failed" -> record.FirstFailedWitness
            | _ -> None

        let providerFailedWitness =
            match record.FirstFailedWitness with
            | Some witness when not (tmStageWitnesses.Contains witness) -> Some witness
            | _ when record.ProviderWitnessStatus = Some "failed" -> record.FirstFailedWitness
            | _ -> None

        let tmAccepted = record.TmWitnessStatus = Some "passed"
        let providerAccepted =
            match record.ProviderWitnessStatus with
            | Some "passed" -> Some true
            | Some "failed" -> Some false
            | _ -> None

        let providerGeneratedModule =
            record.Artifacts |> Option.bind (fun artifacts -> readArtifactText artifacts.ProviderWitnessModule)

        let providerCheckerExcerpt =
            record.Artifacts
            |> Option.bind (fun artifacts ->
                artifacts.ProviderWitnessCheck
                |> Option.bind (fun reference -> checkerExcerptFromPath reference.Path))

        let tmIssues =
            [ match normalizedIntent, definition.ReferenceIntent with
              | None, None ->
                  yield
                      issue
                          "TM_NO_NORMALIZED_INTENT"
                          "The live LLM pipeline did not produce an operational intent in the supported admission subset."
              | _ -> ()
              yield! tmFailedWitness |> Option.bind failedWitnessIssue |> Option.toList
              yield! issuesFromDiagnostics record.Diagnostics ]
            |> distinctIssues

        let dependentTm =
            { Accepted = tmAccepted
              Issues = tmIssues
              FailedWitness = tmFailedWitness }

        let providerIssues =
            [ yield! providerFailedWitness |> Option.bind failedWitnessIssue |> Option.toList
              if tmAccepted && providerAccepted.IsNone then
                  yield issue "PROVIDER_NOT_RUN" "Provider witness construction was not completed."
              yield!
                if tmAccepted then
                    issuesFromDiagnostics record.Diagnostics
                else
                    [] ]
            |> distinctIssues

        let providerDecision =
            if dependentTm.Accepted || record.SelectedProfile.IsSome then
                Some
                    { SelectedProfile =
                        record.SelectedProfile
                        |> Option.orElseWith (fun () -> IntentAdmission.resolveProviderProfileName (restrictedFromDemoIntent workingIntent))
                      Checks = providerIssues }
            else
                None

        let dependentProvider =
            { Accepted = if dependentTm.Accepted then providerAccepted else None
              SelectedProfile = providerDecision |> Option.bind (fun decision -> decision.SelectedProfile)
              Issues = providerIssues
              FailedWitness = if dependentTm.Accepted then providerFailedWitness else None
              CheckerExcerpt = providerCheckerExcerpt
              GeneratedModule = providerGeneratedModule
              AdmissionTokenType =
                match record.AdmissionOutcome, record.SelectedProfile with
                | Some "provider_admitted", Some profile -> Some $"admission_token {profile}"
                | _ -> None }

        let finalOutcome =
            if not dependentTm.Accepted then
                "rejected_tm"
            else
                match dependentProvider.Accepted with
                | Some true -> "accepted"
                | _ -> "rejected_provider"

        let fstarResult =
            match definitionOption, definition.FStarFile, definition.FStarExpectedSuccess with
            | Some _, Some fileName, Some expectedSuccess -> Some(runFStarCase fileName expectedSuccess)
            | _ -> None

        let story = inferStory definitionOption jsonBaseline dependentTm dependentProvider

        let checks =
            match definitionOption with
            | Some scenario -> expectationChecks scenario jsonBaseline dependentTm dependentProvider
            | None -> successfulExpectationChecks

        { Id = definition.Id
          Title = definition.Title
          Kicker = definition.Kicker
          Text = text
          TmAccepted = dependentTm.Accepted
          TmIssues = tmIssues
          NormalizedIntent = normalizedIntent
          ProviderAccepted = dependentProvider.Accepted
          ProviderDecision = providerDecision
          ProviderFStarModule = dependentProvider.GeneratedModule
          FinalOutcome = finalOutcome
          FStarCase = fstarResult
          JsonBaseline = jsonBaseline
          DependentTm = dependentTm
          DependentProvider = dependentProvider
          ConstraintTrace =
            buildConstraintTrace workingIntent jsonBaseline dependentTm dependentProvider dependentProvider.FailedWitness.IsNone
          Story = story
          ExpectedJsonAccepted =
            definitionOption |> Option.map (fun scenario -> scenario.ExpectedJsonAccepted) |> Option.defaultValue jsonBaseline.Accepted
          ExpectedFailedWitness =
            definitionOption
            |> Option.bind (fun scenario -> scenario.ExpectedFailedWitness)
            |> Option.orElse (dependentTm.FailedWitness |> Option.orElse dependentProvider.FailedWitness)
          ExpectationChecks = checks }

    let private referenceDate = DateOnly(2026, 4, 25)

    let private broadcastReference targetName : IntentAdmission.RestrictedIntent =
        { IntentName = "LiveBroadcastIntent"
          ScenarioFamily = IntentAdmission.LiveBroadcast
          TargetName = Some targetName
          TargetKind = Some IntentAdmission.VenueTarget
          ServiceClass = Some "premium-5g-broadcast"
          EventDate = Some referenceDate
          StartHour = Some 18
          EndHour = Some 22
          Timezone = Some "America/Detroit"
          PrimaryDeviceCount = Some 200
          AuxiliaryEndpointCount = None
          MaxLatencyMs = Some 20
          ReportingIntervalMinutes = Some 60
          ImmediateDegradationAlerts = true
          SafetyPolicyDeclared = true
          PreserveEmergencyTraffic = true
          RequestPublicSafetyPreemption = false
          SourceText = None }

    let private criticalReference targetName : IntentAdmission.RestrictedIntent =
        { IntentName = "CriticalServiceIntent"
          ScenarioFamily = IntentAdmission.CriticalService
          TargetName = Some targetName
          TargetKind = Some IntentAdmission.FacilityTarget
          ServiceClass = Some "ultra-reliable-5g-clinical"
          EventDate = Some referenceDate
          StartHour = Some 8
          EndHour = Some 20
          Timezone = Some "America/Detroit"
          PrimaryDeviceCount = Some 80
          AuxiliaryEndpointCount = Some 200
          MaxLatencyMs = Some 10
          ReportingIntervalMinutes = Some 5
          ImmediateDegradationAlerts = true
          SafetyPolicyDeclared = true
          PreserveEmergencyTraffic = true
          RequestPublicSafetyPreemption = false
          SourceText = None }

    let scenarios =
        [ { Id = "broadcast_success_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
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
            ReferenceIntent = Some(broadcastReference "Detroit Stadium")
            FStarFile = Some "BroadcastProviderDemo.Success.fst"
            FStarExpectedSuccess = Some true }
          { Id = "broadcast_fail_window_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
            Title = "Window Witness Failure"
            Kicker = "Scenario 2"
            Text =
              "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 22:00 to 18:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "JSON shape passes and the common-core witness succeeds, but provider window refinement fails."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "window_checked_intent"
            Story =
              "This case is structurally complete enough for JSON validation and TR292/common-core validation, yet provider refinement still rejects it because the time window is semantically impossible."
            ReferenceIntent =
              Some
                { broadcastReference "Detroit Stadium" with
                    StartHour = Some 22
                    EndHour = Some 18 }
            FStarFile = Some "BroadcastProviderDemo.FailWindow.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_capacity_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
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
            ReferenceIntent =
              Some
                { broadcastReference "Detroit Stadium" with
                    PrimaryDeviceCount = Some 2000 }
            FStarFile = Some "BroadcastProviderDemo.FailCapacity.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_latency_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
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
            ReferenceIntent =
              Some
                { broadcastReference "Detroit Stadium" with
                    MaxLatencyMs = Some 5 }
            FStarFile = Some "BroadcastProviderDemo.FailLatency.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_policy_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
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
            ReferenceIntent =
              Some
                { broadcastReference "Detroit Stadium" with
                    PreserveEmergencyTraffic = false
                    RequestPublicSafetyPreemption = true }
            FStarFile = Some "BroadcastProviderDemo.FailPolicy.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_profile_gold_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
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
            ReferenceIntent =
              Some
                { broadcastReference "Detroit Stadium" with
                    PrimaryDeviceCount = Some 90
                    MaxLatencyMs = Some 30 }
            FStarFile = Some "BroadcastProviderDemo.ProfileGold.fst"
            FStarExpectedSuccess = Some true }
          { Id = "broadcast_profile_silver_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
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
            ReferenceIntent =
              Some
                { broadcastReference "Metro Arena" with
                    PrimaryDeviceCount = Some 90
                    MaxLatencyMs = Some 30 }
            FStarFile = Some "BroadcastProviderDemo.ProfileSilver.fst"
            FStarExpectedSuccess = Some false }
          { Id = "broadcast_fail_tm_01"
            ScenarioFamily = IntentAdmission.LiveBroadcast
            Title = "Shape-Level Failure"
            Kicker = "Scenario 8"
            Text = "Make the event network really good and fast for the broadcast."
            ExpectedOutcome = DemoRejectTm
            ExpectedMessage = "The request is too underspecified even for the JSON baseline."
            ExpectedJsonAccepted = false
            ExpectedFailedWitness = Some "measurable_intent"
            Story =
              "This is the easy case for a schema baseline: the request never becomes a measurable normalized object, so both JSON shape checks and dependent witnesses stop immediately."
            ReferenceIntent =
              Some
                { IntentName = "LiveBroadcastIntent"
                  ScenarioFamily = IntentAdmission.LiveBroadcast
                  TargetName = None
                  TargetKind = None
                  ServiceClass = None
                  EventDate = None
                  StartHour = None
                  EndHour = None
                  Timezone = None
                  PrimaryDeviceCount = None
                  AuxiliaryEndpointCount = None
                  MaxLatencyMs = None
                  ReportingIntervalMinutes = None
                  ImmediateDegradationAlerts = false
                  SafetyPolicyDeclared = false
                  PreserveEmergencyTraffic = false
                  RequestPublicSafetyPreemption = false
                  SourceText = None }
            FStarFile = Some "BroadcastProviderDemo.FailTm.fst"
            FStarExpectedSuccess = Some false }
          { Id = "critical_success_01"
            ScenarioFamily = IntentAdmission.CriticalService
            Title = "Accepted Critical Service"
            Kicker = "Scenario 9"
            Text =
              "Provide an ultra-reliable 5G clinical service for telemedicine and critical care operations at Mayo Clinic on April 25, 2026 from 08:00 to 20:00 America/Detroit. Support up to 80 critical devices and 200 auxiliary endpoints. Maintain end-to-end latency below 10 ms. Send compliance updates every 5 minutes and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoAccept
            ExpectedMessage = "Accepted with CriticalCareAssured and a downstream admission token."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = None
            Story =
              "This is the critical-service success path: the request is specific enough for the TR292/common-core witnesses and still fits the assured clinical provider envelope."
            ReferenceIntent = Some(criticalReference "Mayo Clinic")
            FStarFile = Some "CriticalServiceDemo.Success.fst"
            FStarExpectedSuccess = Some true }
          { Id = "critical_fail_capacity_01"
            ScenarioFamily = IntentAdmission.CriticalService
            Title = "Critical Capacity Failure"
            Kicker = "Scenario 10"
            Text =
              "Provide an ultra-reliable 5G clinical service for telemedicine and critical care operations at Mayo Clinic on April 25, 2026 from 08:00 to 20:00 America/Detroit. Support up to 95 critical devices and 260 auxiliary endpoints. Maintain end-to-end latency below 10 ms. Send compliance updates every 5 minutes and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "JSON shape passes, but clinical provider capacity constraints fail."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "capacity_checked_intent"
            Story =
              "The critical-service family also needs provider refinement: the assured clinical profile rejects overloads that still look structurally valid in JSON."
            ReferenceIntent =
              Some
                { criticalReference "Mayo Clinic" with
                    PrimaryDeviceCount = Some 95
                    AuxiliaryEndpointCount = Some 260 }
            FStarFile = Some "CriticalServiceDemo.FailCapacity.fst"
            FStarExpectedSuccess = Some false }
          { Id = "critical_fail_latency_01"
            ScenarioFamily = IntentAdmission.CriticalService
            Title = "Critical Latency Failure"
            Kicker = "Scenario 11"
            Text =
              "Provide an ultra-reliable 5G clinical service for telemedicine and critical care operations at Mayo Clinic on April 25, 2026 from 08:00 to 20:00 America/Detroit. Support up to 80 critical devices and 200 auxiliary endpoints. Maintain end-to-end latency below 5 ms. Send compliance updates every 5 minutes and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "JSON shape passes, but the clinical latency-floor witness fails."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "latency_checked_intent"
            Story =
              "This request is semantically precise, yet the assured provider profile still rejects it because the claimed latency undercuts the supported floor."
            ReferenceIntent =
              Some
                { criticalReference "Mayo Clinic" with
                    MaxLatencyMs = Some 5 }
            FStarFile = Some "CriticalServiceDemo.FailLatency.fst"
            FStarExpectedSuccess = Some false }
          { Id = "critical_fail_policy_01"
            ScenarioFamily = IntentAdmission.CriticalService
            Title = "Critical Policy Failure"
            Kicker = "Scenario 12"
            Text =
              "Provide an ultra-reliable 5G clinical service for telemedicine and critical care operations at Mayo Clinic on April 25, 2026 from 08:00 to 20:00 America/Detroit. Support up to 80 critical devices and 200 auxiliary endpoints. Maintain end-to-end latency below 10 ms. Send compliance updates every 5 minutes and immediate alerts if service quality degrades. If needed, preempt reserved public-safety capacity to maintain service."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "JSON shape passes, but the critical-service policy witness fails."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "policy_checked_intent"
            Story =
              "The protected-traffic policy also matters in the critical-service family, so the witness chain still stops before provider admission."
            ReferenceIntent =
              Some
                { criticalReference "Mayo Clinic" with
                    PreserveEmergencyTraffic = false
                    RequestPublicSafetyPreemption = true }
            FStarFile = Some "CriticalServiceDemo.FailPolicy.fst"
            FStarExpectedSuccess = Some false }
          { Id = "critical_profile_standard_01"
            ScenarioFamily = IntentAdmission.CriticalService
            Title = "Standard Clinical Profile Rejects Same Shape"
            Kicker = "Scenario 13"
            Text =
              "Provide an ultra-reliable 5G clinical service for telemedicine and critical care operations at City General Hospital on April 25, 2026 from 08:00 to 20:00 America/Detroit. Support up to 55 critical devices and 110 auxiliary endpoints. Maintain end-to-end latency below 12 ms. Send compliance updates every 10 minutes and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
            ExpectedOutcome = DemoRejectProvider
            ExpectedMessage = "The same clinical shape fails once the provider profile changes."
            ExpectedJsonAccepted = true
            ExpectedFailedWitness = Some "latency_checked_intent"
            Story =
              "Only the facility-to-profile mapping changes here. The request is clinically well formed, but the standard provider profile cannot satisfy the tighter latency claim."
            ReferenceIntent =
              Some
                { criticalReference "City General Hospital" with
                    PrimaryDeviceCount = Some 55
                    AuxiliaryEndpointCount = Some 110
                    MaxLatencyMs = Some 12
                    ReportingIntervalMinutes = Some 10 }
            FStarFile = Some "CriticalServiceDemo.ProfileStandard.fst"
            FStarExpectedSuccess = Some false }
          { Id = "critical_fail_tm_01"
            ScenarioFamily = IntentAdmission.CriticalService
            Title = "Critical Shape-Level Failure"
            Kicker = "Scenario 14"
            Text = "Keep the telemedicine network ultra reliable for the hospital."
            ExpectedOutcome = DemoRejectTm
            ExpectedMessage = "The critical-service request is too underspecified for TR292/common-core admission."
            ExpectedJsonAccepted = false
            ExpectedFailedWitness = Some "measurable_intent"
            Story =
              "The clinical family fails early for the same reason as the vague broadcast case: there is no measurable target, window, or typed quantity to compose into a witness."
            ReferenceIntent =
              Some
                { IntentName = "CriticalServiceIntent"
                  ScenarioFamily = IntentAdmission.CriticalService
                  TargetName = None
                  TargetKind = None
                  ServiceClass = None
                  EventDate = None
                  StartHour = None
                  EndHour = None
                  Timezone = None
                  PrimaryDeviceCount = None
                  AuxiliaryEndpointCount = None
                  MaxLatencyMs = None
                  ReportingIntervalMinutes = None
                  ImmediateDegradationAlerts = false
                  SafetyPolicyDeclared = false
                  PreserveEmergencyTraffic = false
                  RequestPublicSafetyPreemption = false
                  SourceText = None }
            FStarFile = Some "CriticalServiceDemo.FailTm.fst"
            FStarExpectedSuccess = Some false } ]

    let featuredScenarioIds =
        [ "broadcast_success_01"
          "broadcast_fail_capacity_01"
          "broadcast_fail_policy_01"
          "broadcast_fail_tm_01"
          "critical_success_01"
          "critical_fail_capacity_01"
          "critical_fail_policy_01"
          "critical_profile_standard_01"
          "critical_fail_tm_01" ]

    let tryFindScenario id =
        scenarios |> List.tryFind (fun scenario -> scenario.Id = id)

    let featuredScenarios =
        featuredScenarioIds |> List.choose tryFindScenario

    let tryReadFStarSource fileName =
        let filePath = Path.Combine(fstarDemoDir (), fileName)
        if File.Exists filePath then Some(File.ReadAllText filePath) else None

    let buildNaturalLanguageRequest (text: string) : IntentFvo =
        let expressionValue = JsonDocument.Parse(JsonSerializer.Serialize(text)).RootElement.Clone()
        let contextText =
            let lower = text.ToLowerInvariant()

            if
                lower.Contains("telemedicine")
                || lower.Contains("clinical")
                || lower.Contains("critical care")
                || lower.Contains("hospital")
            then
                "Critical service assurance"
            else
                "Live broadcast service"

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
          Context = Some contextText
          Version = Some "1.0"
          IntentSpecification = None
          LifecycleStatus = Some "acknowledged"
          Type = Some "Intent"
          BaseType = None
          SchemaLocation = None }

    let validateWithLivePipelineAsync
        (rawIntentGenerator: IRawIntentGenerator)
        (definitionOption: DemoScenarioDefinition option)
        (text: string) =
        task {
            let intentId = $"demo-{Guid.NewGuid():N}"
            let generator = generatorForDefinition rawIntentGenerator definitionOption

            let! outcome =
                IntentPipeline.processIntentWithContextAsync
                    generator
                    RawIntentGenerationContext.Live
                    intentId
                    (buildNaturalLanguageRequest text)

            let validation =
                evaluateProcessedScenario definitionOption outcome.ProcessingRecord.RequestId text outcome.ProcessingRecord

            let definition =
                definitionOption |> Option.defaultWith (fun () -> adHocDefinition text)

            return validation, outcome.ProcessingRecord, definition
        }

    let runScenarioAsync (rawIntentGenerator: IRawIntentGenerator) (definition: DemoScenarioDefinition) =
        task {
            let! validation, _, _ = validateWithLivePipelineAsync rawIntentGenerator (Some definition) definition.Text
            return validation
        }

    let runAllAsync (rawIntentGenerator: IRawIntentGenerator) =
        task {
            let results = ResizeArray<DemoScenarioResult>()

            for scenario in scenarios do
                let! result = runScenarioAsync rawIntentGenerator scenario
                results.Add result

            return results |> Seq.toList
        }
