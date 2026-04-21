namespace Tmf921.IntentManagement.Api

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

module IntentAdmission =
    type IntentScenarioFamily =
        | LiveBroadcast
        | CriticalService

    type IntentTargetKind =
        | VenueTarget
        | FacilityTarget

    type ProviderProfile =
        | LiveBroadcastSilver
        | LiveBroadcastGold
        | CriticalCareAssured
        | CriticalCareStandard
        | UnsupportedProfile

    type RestrictedIntent =
        { IntentName: string
          ScenarioFamily: IntentScenarioFamily
          TargetName: string option
          TargetKind: IntentTargetKind option
          ServiceClass: string option
          EventDate: DateOnly option
          StartHour: int option
          EndHour: int option
          Timezone: string option
          PrimaryDeviceCount: int option
          AuxiliaryEndpointCount: int option
          MaxLatencyMs: int option
          ReportingIntervalMinutes: int option
          ImmediateDegradationAlerts: bool
          SafetyPolicyDeclared: bool
          PreserveEmergencyTraffic: bool
          RequestPublicSafetyPreemption: bool
          SourceText: string option }

    type ParsedCandidateModule =
        { ModuleName: string
          IntentBindingName: string
          Intent: RestrictedIntent
          SourceText: string }

    type WitnessCheckResult =
        { Success: bool
          CheckerVersion: string option
          ModuleText: string
          Stdout: string
          Stderr: string
          Diagnostics: ProcessingDiagnostic list }

    type AdmissionCheckBundle =
        { CandidateModuleName: string
          CandidateCheck: WitnessCheckResult
          TmWitness: WitnessCheckResult option
          ProviderWitness: WitnessCheckResult option
          SelectedProfile: string option
          FirstFailedWitness: string option
          TmWitnessStatus: string
          ProviderWitnessStatus: string
          AdmissionOutcome: string }

    let private commonCoreProfile =
        { Domain.defaultOntologyProfile with
            Name = "tmforum.tr292-common-core"
            EnabledModules =
                [ "TR290A"
                  "TR290V"
                  "TR292A"
                  "TR292C"
                  "TR292D"
                  "TR292E"
                  "TR292G"
                  "TR292I" ] }

    let private diagnostic code message details =
        { Code = code
          Message = message
          Details = details }

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

    let familyName family =
        match family with
        | LiveBroadcast -> "LiveBroadcast"
        | CriticalService -> "CriticalService"

    let targetKindName value =
        match value with
        | VenueTarget -> "VenueTarget"
        | FacilityTarget -> "FacilityTarget"

    let profileName value =
        match value with
        | LiveBroadcastSilver -> "LiveBroadcastSilver"
        | LiveBroadcastGold -> "LiveBroadcastGold"
        | CriticalCareAssured -> "CriticalCareAssured"
        | CriticalCareStandard -> "CriticalCareStandard"
        | UnsupportedProfile -> "UnsupportedProfile"

    let resolveProviderProfile intent =
        match intent.ScenarioFamily, intent.TargetName with
        | LiveBroadcast, Some "Detroit Stadium" -> LiveBroadcastGold
        | LiveBroadcast, Some "Metro Arena" -> LiveBroadcastSilver
        | CriticalService, Some "Mayo Clinic" -> CriticalCareAssured
        | CriticalService, Some "City General Hospital" -> CriticalCareStandard
        | _ -> UnsupportedProfile

    let resolveProviderProfileName intent =
        match resolveProviderProfile intent with
        | UnsupportedProfile -> None
        | profile -> Some(profileName profile)

    let private familyModuleValue family =
        match family with
        | LiveBroadcast -> "BroadcastFamily"
        | CriticalService -> "CriticalServiceFamily"

    let private targetKindModuleValue value =
        match value with
        | VenueTarget -> "VenueTarget"
        | FacilityTarget -> "FacilityTarget"

    let private profileModuleValue value =
        match value with
        | LiveBroadcastSilver -> "LiveBroadcastSilver"
        | LiveBroadcastGold -> "LiveBroadcastGold"
        | CriticalCareAssured -> "CriticalCareAssured"
        | CriticalCareStandard -> "CriticalCareStandard"
        | UnsupportedProfile -> "UnsupportedProfile"

    let private parseFamily (value: string) =
        match value.Trim() with
        | "BroadcastFamily" -> Some LiveBroadcast
        | "CriticalServiceFamily" -> Some CriticalService
        | _ -> None

    let private parseTargetKind (value: string) =
        match value.Trim() with
        | "VenueTarget" -> Some VenueTarget
        | "FacilityTarget" -> Some FacilityTarget
        | _ -> None

    let private profileForIntent intent =
        match intent.ScenarioFamily, intent.TargetName with
        | LiveBroadcast, Some "Detroit Stadium" -> LiveBroadcastGold
        | LiveBroadcast, Some "Metro Arena" -> LiveBroadcastSilver
        | CriticalService, Some "Mayo Clinic" -> CriticalCareAssured
        | CriticalService, Some "City General Hospital" -> CriticalCareStandard
        | _ -> UnsupportedProfile

    let private maxPrimaryDevices profile =
        match profile with
        | LiveBroadcastSilver -> 100
        | LiveBroadcastGold -> 250
        | CriticalCareAssured -> 80
        | CriticalCareStandard -> 60
        | UnsupportedProfile -> 0

    let private maxAuxiliaryEndpoints profile =
        match profile with
        | LiveBroadcastSilver
        | LiveBroadcastGold -> Int32.MaxValue
        | CriticalCareAssured -> 220
        | CriticalCareStandard -> 120
        | UnsupportedProfile -> 0

    let private minLatencyBound profile =
        match profile with
        | LiveBroadcastSilver -> 40
        | LiveBroadcastGold -> 20
        | CriticalCareAssured -> 10
        | CriticalCareStandard -> 15
        | UnsupportedProfile -> 0

    let private providerWindowOk intent =
        match intent.ScenarioFamily, intent.StartHour, intent.EndHour with
        | LiveBroadcast, Some startHour, Some endHour -> startHour >= 6 && endHour <= 23
        | CriticalService, Some startHour, Some endHour -> startHour >= 0 && endHour <= 23
        | _ -> false

    let private providerReportingMinimum profile =
        match profile with
        | LiveBroadcastSilver
        | LiveBroadcastGold -> 15
        | CriticalCareAssured -> 5
        | CriticalCareStandard -> 10
        | UnsupportedProfile -> Int32.MaxValue

    let private reportingOk profile intent =
        match intent.ReportingIntervalMinutes with
        | Some minutes -> minutes >= providerReportingMinimum profile
        | None -> false

    let private windowOk intent =
        match intent.StartHour, intent.EndHour with
        | Some startHour, Some endHour -> startHour < endHour
        | _ -> false

    let private measurableMissing intent =
        let commonMissing =
            intent.TargetName.IsNone
            || intent.TargetKind.IsNone
            || intent.ServiceClass.IsNone
            || intent.EventDate.IsNone
            || intent.StartHour.IsNone
            || intent.EndHour.IsNone
            || intent.Timezone.IsNone
            || intent.PrimaryDeviceCount.IsNone
            || intent.MaxLatencyMs.IsNone
            || intent.ReportingIntervalMinutes.IsNone

        let familyMissing =
            match intent.ScenarioFamily with
            | LiveBroadcast -> false
            | CriticalService -> intent.AuxiliaryEndpointCount.IsNone

        commonMissing || familyMissing

    let private quantityIssues intent =
        [ match intent.PrimaryDeviceCount with
          | Some value when value > 0 -> ()
          | Some _ -> yield "quantity_checked_intent"
          | None -> ()
          match intent.AuxiliaryEndpointCount with
          | Some value when value > 0 -> ()
          | Some _ -> yield "quantity_checked_intent"
          | None when intent.ScenarioFamily = CriticalService -> yield "quantity_checked_intent"
          | None -> ()
          match intent.MaxLatencyMs with
          | Some value when value > 0 -> ()
          | Some _ -> yield "quantity_checked_intent"
          | None -> ()
          match intent.ReportingIntervalMinutes with
          | Some value when value > 0 -> ()
          | Some _ -> yield "quantity_checked_intent"
          | None -> () ]

    let tmFailedWitness intent =
        if measurableMissing intent then
            Some "measurable_intent"
        else if not (quantityIssues intent |> List.isEmpty) then
            Some "quantity_checked_intent"
        else
            None

    let providerFailedWitness intent =
        match tmFailedWitness intent with
        | Some _ -> None
        | None ->
            if not (windowOk intent) then
                Some "window_checked_intent"
            else
                let profile = profileForIntent intent

                if profile = UnsupportedProfile then
                    Some "profiled_intent"
                else
                    let capacityFails =
                        not (providerWindowOk intent)
                        || not (reportingOk profile intent)
                        ||
                           match intent.PrimaryDeviceCount with
                           | Some value -> value > maxPrimaryDevices profile
                           | None -> true
                        ||
                           match intent.AuxiliaryEndpointCount with
                           | Some value -> value > maxAuxiliaryEndpoints profile
                           | None when intent.ScenarioFamily = CriticalService -> true
                           | None -> false

                    let latencyFails =
                        match intent.MaxLatencyMs with
                        | Some value -> value < minLatencyBound profile
                        | None -> true

                    let policyFails =
                        not intent.ImmediateDegradationAlerts
                        || not intent.SafetyPolicyDeclared
                        || intent.RequestPublicSafetyPreemption
                        || not intent.PreserveEmergencyTraffic

                    if capacityFails then
                        Some "capacity_checked_intent"
                    else if latencyFails then
                        Some "latency_checked_intent"
                    else if policyFails then
                        Some "policy_checked_intent"
                    else
                        None

    let toOperationalIntentRecord intent =
        { IntentName = intent.IntentName
          ScenarioFamily = Some(familyName intent.ScenarioFamily)
          TargetName = intent.TargetName
          TargetKind = intent.TargetKind |> Option.map targetKindName
          ServiceClass = intent.ServiceClass
          EventDate =
            intent.EventDate
            |> Option.map (fun value -> value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
          StartHour = intent.StartHour
          EndHour = intent.EndHour
          Timezone = intent.Timezone
          PrimaryDeviceCount = intent.PrimaryDeviceCount
          AuxiliaryEndpointCount = intent.AuxiliaryEndpointCount
          MaxLatencyMs = intent.MaxLatencyMs
          ReportingIntervalMinutes = intent.ReportingIntervalMinutes
          ImmediateDegradationAlerts = intent.ImmediateDegradationAlerts
          SafetyPolicyDeclared = intent.SafetyPolicyDeclared
          PreserveEmergencyTraffic = intent.PreserveEmergencyTraffic
          RequestPublicSafetyPreemption = intent.RequestPublicSafetyPreemption }

    let tryFromOperationalIntentRecord (record: OperationalIntentRecord) =
        let tryParseFamily value =
            match trimToOption value with
            | Some "LiveBroadcast" -> Some LiveBroadcast
            | Some "CriticalService" -> Some CriticalService
            | _ -> None

        let tryParseTargetKind value =
            match trimToOption value with
            | Some "VenueTarget" -> Some VenueTarget
            | Some "FacilityTarget" -> Some FacilityTarget
            | _ -> None

        let tryParseDate value =
            value
            |> trimToOption
            |> Option.bind (fun text ->
                match DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None) with
                | true, parsed -> Some parsed
                | _ -> None)

        match tryParseFamily record.ScenarioFamily with
        | None ->
            Error
                [ diagnostic
                    "UNSUPPORTED_OPERATIONAL_INTENT"
                    "The operational intent record did not identify a supported scenario family."
                    record.ScenarioFamily ]
        | Some scenarioFamily ->
            Ok
                { IntentName = record.IntentName
                  ScenarioFamily = scenarioFamily
                  TargetName = trimToOption record.TargetName
                  TargetKind = tryParseTargetKind record.TargetKind
                  ServiceClass = trimToOption record.ServiceClass
                  EventDate = tryParseDate record.EventDate
                  StartHour = record.StartHour
                  EndHour = record.EndHour
                  Timezone = trimToOption record.Timezone
                  PrimaryDeviceCount = record.PrimaryDeviceCount
                  AuxiliaryEndpointCount = record.AuxiliaryEndpointCount
                  MaxLatencyMs = record.MaxLatencyMs
                  ReportingIntervalMinutes = record.ReportingIntervalMinutes
                  ImmediateDegradationAlerts = record.ImmediateDegradationAlerts
                  SafetyPolicyDeclared = record.SafetyPolicyDeclared
                  PreserveEmergencyTraffic = record.PreserveEmergencyTraffic
                  RequestPublicSafetyPreemption = record.RequestPublicSafetyPreemption
                  SourceText = None }

    let toCanonicalIntent intent =
        let targetName = intent.TargetName |> Option.defaultValue "unresolved-target"
        let targetType =
            intent.TargetKind
            |> Option.map (function
                | VenueTarget -> "venue"
                | FacilityTarget -> "facility")

        let mkQuantity (value: int) (unitName: string) : CanonicalQuantity =
            { Value = string value
              Unit = Some unitName }

        let mkCondition (kind: string) (subject: string option) (operator: string option) (value: string option) : CanonicalCondition =
            { Kind = kind
              Subject = subject
              Operator = operator
              Value = value
              Children = [] }

        let expectation
            (kind: string)
            (subject: string)
            (description: string)
            (condition: CanonicalCondition option)
            (quantity: CanonicalQuantity option)
            : CanonicalExpectation =
            { Kind = kind
              Subject = subject
              Description = Some description
              Condition = condition
              Quantity = quantity
              FunctionApplication = None }

        let expectations : CanonicalExpectation list =
            [ let defaultServiceClass =
                  match intent.ScenarioFamily with
                  | LiveBroadcast -> intent.ServiceClass |> Option.defaultValue "broadcast"
                  | CriticalService -> intent.ServiceClass |> Option.defaultValue "critical-care"

              let serviceDescription =
                  match intent.ScenarioFamily with
                  | LiveBroadcast -> $"Provide {defaultServiceClass} service for {targetName}."
                  | CriticalService -> $"Provide {defaultServiceClass} service for {targetName}."

              let windowValue =
                  match intent.EventDate, intent.StartHour, intent.EndHour, intent.Timezone with
                  | Some dateValue, Some startHour, Some endHour, Some timezone ->
                      let dateText = dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                      let startText = startHour.ToString("00", CultureInfo.InvariantCulture)
                      let endText = endHour.ToString("00", CultureInfo.InvariantCulture)
                      Some
                          $"{dateText} {startText}:00 to {dateText} {endText}:00 {timezone}"
                  | _ -> None

              yield
                  expectation
                      "service"
                      (intent.ServiceClass |> Option.defaultValue "service")
                      serviceDescription
                      (windowValue |> Option.map (fun value -> mkCondition "time_window" (Some "service_period") (Some "between") (Some value)))
                      None

              match intent.ScenarioFamily, intent.PrimaryDeviceCount with
              | LiveBroadcast, Some count ->
                  yield
                      expectation
                          "capacity"
                          "production_devices"
                          $"Support up to {count} production devices."
                          None
                          (Some(mkQuantity count "devices"))
              | CriticalService, Some count ->
                  yield
                      expectation
                          "capacity"
                          "critical_devices"
                          $"Support up to {count} critical devices."
                          None
                          (Some(mkQuantity count "devices"))
              | _ -> ()

              match intent.ScenarioFamily, intent.AuxiliaryEndpointCount with
              | CriticalService, Some count ->
                  yield
                      expectation
                          "capacity"
                          "auxiliary_endpoints"
                          $"Support up to {count} auxiliary endpoints."
                          None
                          (Some(mkQuantity count "endpoints"))
              | _ -> ()

              match intent.MaxLatencyMs with
              | Some latency ->
                  yield
                      expectation
                          "latency"
                          (match intent.ScenarioFamily with
                           | LiveBroadcast -> "uplink_latency"
                           | CriticalService -> "end_to_end_latency")
                          $"Keep latency under {latency} ms."
                          (Some(mkCondition "threshold" (Some "latency") (Some "under") (Some $"{latency} ms")))
                          (Some(mkQuantity latency "ms"))
              | None -> ()

              match intent.ReportingIntervalMinutes with
              | Some minutes ->
                  yield
                      expectation
                          "reporting"
                          (match intent.ScenarioFamily with
                           | LiveBroadcast -> "compliance_updates"
                           | CriticalService -> "clinical_assurance_updates")
                          $"Send updates every {minutes} minutes."
                          None
                          (Some(mkQuantity minutes "minutes"))
              | None -> ()

              if intent.ImmediateDegradationAlerts then
                  yield
                      expectation
                          "alerting"
                          "service_quality"
                          "Send immediate alerts if service quality degrades."
                          (Some(mkCondition "event" (Some "service_quality") (Some "degrades") (Some "true")))
                          None

              if intent.SafetyPolicyDeclared then
                  let description =
                      if intent.RequestPublicSafetyPreemption then
                          "If needed, preempt reserved public-safety capacity to maintain service."
                      else
                          "Do not impact emergency-service traffic."

                  let operatorValue =
                      if intent.RequestPublicSafetyPreemption then "preempt_if_needed" else "preserve"

                  yield
                      expectation
                          "protection"
                          "emergency_service_traffic"
                          description
                          (Some(mkCondition "policy" (Some "emergency_service_traffic") (Some operatorValue) (Some "true")))
                          None ]

        { IntentName = intent.IntentName
          Description = intent.SourceText |> Option.orElse (Some $"Restricted F* intent for {targetName}.")
          Targets =
            [ { Id = targetName
                TargetType = targetType
                Name = intent.TargetName } ]
          Expectations = expectations
          Context =
            Some(
                match intent.ScenarioFamily with
                | LiveBroadcast -> "live broadcast service"
                | CriticalService -> "critical service assurance"
            )
          Priority =
            Some(
                match intent.ScenarioFamily with
                | LiveBroadcast -> "premium"
                | CriticalService -> "assured"
            )
          Profile = commonCoreProfile
          SourceClassification = InputKind.NaturalLanguage
          SourceText = intent.SourceText
          SourceIri = Some "urn:tmf921:admission:fstar"
          RawExpressionType = Some "FStarExpression" }

    let emitJsonLd (canonical: CanonicalIntentIr) =
        let canonicalContext =
            {| icm = "http://www.models.tmforum.org/tio/v1.0.0/IntentCommonModel#"
               tio = "http://www.models.tmforum.org/tio/v1.0.0#"
               quan = "http://www.models.tmforum.org/tio/v1.0.0/QuantityOntology#"
               funn = "http://www.models.tmforum.org/tio/v1.0.0/FunctionOntology#" |}

        let expectationNodes =
            canonical.Expectations
            |> List.map (fun expectation ->
                let obj = JsonObject()
                obj["@type"] <- JsonValue.Create($"icm:{expectation.Kind}")
                obj["icm:subject"] <- JsonValue.Create(expectation.Subject)
                expectation.Description |> Option.iter (fun value -> obj["icm:description"] <- JsonValue.Create(value))

                expectation.Quantity
                |> Option.iter (fun quantity ->
                    let q = JsonObject()
                    q["@type"] <- JsonValue.Create("quan:Quantity")
                    q["rdf:value"] <- JsonValue.Create(quantity.Value)
                    quantity.Unit |> Option.iter (fun unitName -> q["quan:unit"] <- JsonValue.Create(unitName))
                    obj["icm:quantity"] <- q)

                expectation.Condition
                |> Option.iter (fun condition ->
                    let rec toNode (value: CanonicalCondition) =
                        let conditionObject = JsonObject()
                        conditionObject["tio:kind"] <- JsonValue.Create(value.Kind)
                        value.Subject |> Option.iter (fun text -> conditionObject["tio:subject"] <- JsonValue.Create(text))
                        value.Operator |> Option.iter (fun text -> conditionObject["tio:operator"] <- JsonValue.Create(text))
                        value.Value |> Option.iter (fun text -> conditionObject["tio:value"] <- JsonValue.Create(text))

                        if not value.Children.IsEmpty then
                            let children = JsonArray()
                            value.Children |> List.iter (toNode >> children.Add)
                            conditionObject["tio:children"] <- children

                        conditionObject :> JsonNode

                    obj["icm:condition"] <- toNode condition)

                obj :> JsonNode)

        let targetNodes =
            canonical.Targets
            |> List.map (fun target ->
                let obj = JsonObject()
                obj["@id"] <- JsonValue.Create(target.Id)
                target.TargetType |> Option.iter (fun targetType -> obj["@type"] <- JsonValue.Create(targetType))
                target.Name |> Option.iter (fun name -> obj["icm:name"] <- JsonValue.Create(name))
                obj :> JsonNode)

        let generatedIntentId =
            canonical.IntentName.ToLowerInvariant().Replace(" ", "-")

        let intentId =
            canonical.SourceIri |> Option.defaultValue $"urn:tmf921:intent:{generatedIntentId}"

        let intent = JsonObject()
        intent["@id"] <- JsonValue.Create(intentId)
        intent["@type"] <- JsonValue.Create("icm:Intent")
        intent["icm:name"] <- JsonValue.Create(canonical.IntentName)
        canonical.Description |> Option.iter (fun value -> intent["icm:description"] <- JsonValue.Create(value))
        canonical.Context |> Option.iter (fun value -> intent["icm:context"] <- JsonValue.Create(value))
        canonical.Priority |> Option.iter (fun value -> intent["icm:priority"] <- JsonValue.Create(value))
        intent["icm:target"] <- JsonArray(targetNodes |> List.toArray)
        intent["icm:expectation"] <- JsonArray(expectationNodes |> List.toArray)
        intent["tmf:profileName"] <- JsonValue.Create(canonical.Profile.Name)
        intent["tmf:profileVersion"] <- JsonValue.Create(canonical.Profile.Version)

        let root = JsonObject()
        root["@context"] <- JsonSerializer.SerializeToNode(canonicalContext, serializerOptions)
        root["@graph"] <- JsonArray([| intent :> JsonNode |])
        JsonDocument.Parse(root.ToJsonString()).RootElement.Clone()

    let private expectationTexts (expectation: CanonicalExpectation) =
        [ Some expectation.Kind
          Some expectation.Subject
          expectation.Description
          expectation.Condition |> Option.map (fun condition -> condition.Kind)
          expectation.Condition |> Option.bind (fun condition -> condition.Subject)
          expectation.Condition |> Option.bind (fun condition -> condition.Operator)
          expectation.Condition |> Option.bind (fun condition -> condition.Value)
          expectation.Quantity |> Option.map (fun quantity -> quantity.Value)
          expectation.Quantity |> Option.bind (fun quantity -> quantity.Unit) ]
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
        let normalizedTexts = expectationTexts expectation |> List.map normalizeToken

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
        | timeText :: remainder -> None, tryParseHourText timeText, parseTimezone remainder
        | _ -> None, None, None

    let private tryParseWindowValue (value: string) =
        match splitOnce " to " (value.Trim()) with
        | None -> None
        | Some(startText, endText) ->
            let startParts = startText.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
            let endParts = endText.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
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
            let dateText = text.Substring(onIndex + 4, fromIndex - (onIndex + 4)).Trim().TrimEnd('.')
            let startText = text.Substring(fromIndex + 6, toIndex - (fromIndex + 6)).Trim()
            let endText = text.Substring(toIndex + 4).Trim().TrimEnd('.')
            let endParts = endText.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
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
        |> Option.orElseWith (fun () -> canonicalTexts canonical |> List.tryPick tryExtractWindowFromText)

    let private tryParseIntText (value: string option) =
        value
        |> trimToOption
        |> Option.bind (fun text ->
            match Int32.TryParse text with
            | true, parsed -> Some parsed
            | _ -> None)

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
                | Some value when String.Equals(value, "hourly", StringComparison.OrdinalIgnoreCase) -> Some 60
                | Some value when String.Equals(value, "daily", StringComparison.OrdinalIgnoreCase) -> Some 1440
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
        canonical.Expectations |> List.collect expectationTexts

    let tryInterpretCanonicalIntent text canonical =
        let joinedText = String.concat " " (canonicalTexts canonical)

        let family =
            if containsText "broadcast" joinedText then
                Some LiveBroadcast
            else if
                containsText "telemedicine" joinedText
                || containsText "critical care" joinedText
                || containsText "clinical" joinedText
                || containsText "mayo clinic" joinedText
            then
                Some CriticalService
            else
                None

        let targetName =
            canonical.Targets
            |> List.tryPick (fun target ->
                target.Name
                |> trimToOption
                |> Option.orElseWith (fun () ->
                    if containsText "urn:" target.Id then None else Some target.Id))

        let eventDate, startHour, endHour, timezone = tryExtractWindow canonical |> Option.defaultValue(None, None, None, None)
        let primaryCapacity = tryFindExpectation [ "capacity"; "productiondevices"; "criticaldevices"; "devices" ] canonical
        let auxiliaryCapacity = tryFindExpectation [ "auxiliary"; "auxiliaryendpoints"; "endpoints" ] canonical
        let latencyExpectation = tryFindExpectation [ "latency"; "endtoendlatency"; "uplinklatency"; "uplink" ] canonical
        let reportingExpectation = tryFindExpectation [ "reporting"; "updates"; "monitoring" ] canonical
        let alertExpectation = tryFindExpectation [ "alerting"; "servicequality"; "degradation" ] canonical

        let preserveEmergencyTraffic =
            collectPolicyTexts canonical
            |> List.exists (fun value ->
                containsText "emergency-service traffic" value
                || containsText "emergency service traffic" value)
            && not (
                collectPolicyTexts canonical
                |> List.exists (fun value -> containsText "preempt" value && containsText "public-safety" value)
            )

        let requestPublicSafetyPreemption =
            collectPolicyTexts canonical
            |> List.exists (fun value ->
                containsText "preempt" value
                || containsText "public-safety" value
                || containsText "public safety" value)

        let serviceClass =
            match family with
            | Some LiveBroadcast when containsText "premium" joinedText || containsText "broadcast" joinedText ->
                Some "premium-5g-broadcast"
            | Some CriticalService when containsText "critical care" joinedText || containsText "telemedicine" joinedText ->
                Some "ultra-reliable-5g-clinical"
            | _ -> None

        match family with
        | None ->
            Error
                [ diagnostic
                    "UNSUPPORTED_CANONICAL_INTENT"
                    "The canonical intent does not map into the restricted F* admission subset."
                    None ]
        | Some scenarioFamily ->
            Ok
                { IntentName = canonical.IntentName
                  ScenarioFamily = scenarioFamily
                  TargetName = targetName
                  TargetKind =
                    Some(
                        match scenarioFamily with
                        | LiveBroadcast -> VenueTarget
                        | CriticalService -> FacilityTarget
                    )
                  ServiceClass = serviceClass
                  EventDate = eventDate
                  StartHour = startHour
                  EndHour = endHour
                  Timezone = timezone |> trimToOption
                  PrimaryDeviceCount = primaryCapacity |> Option.bind parseQuantityValue
                  AuxiliaryEndpointCount = auxiliaryCapacity |> Option.bind parseQuantityValue
                  MaxLatencyMs = latencyExpectation |> Option.bind parseQuantityValue
                  ReportingIntervalMinutes = reportingExpectation |> Option.bind parseReportingMinutes
                  ImmediateDegradationAlerts = alertExpectation.IsSome
                  SafetyPolicyDeclared = preserveEmergencyTraffic || requestPublicSafetyPreemption
                  PreserveEmergencyTraffic = preserveEmergencyTraffic
                  RequestPublicSafetyPreemption = requestPublicSafetyPreemption
                  SourceText = Some text }

    let sanitizeModuleSegment value =
        let sanitized =
            value
            |> Seq.filter (fun ch -> Char.IsLetterOrDigit ch || ch = '_')
            |> Seq.toArray
            |> String

        if String.IsNullOrWhiteSpace sanitized then "Intent" else sanitized

    let private escapeFStarString (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")

    let private renderOptionString value =
        match value with
        | Some text -> $"Some \"{escapeFStarString text}\""
        | None -> "None"

    let private renderOptionNat value =
        match value with
        | Some number when number >= 0 -> $"Some {number}"
        | _ -> "None"

    let private renderFamilyValue value =
        familyModuleValue value

    let private renderTargetKindValue value =
        match value with
        | Some targetKind -> $"Some {targetKindModuleValue targetKind}"
        | None -> "None"

    let buildCandidateModule moduleName intent =
        let bindingName = "candidate_intent"
        $"""module {moduleName}

open TmForumTr292CommonCore

let {bindingName} : raw_tm_intent =
  {{ intent_name = "{escapeFStarString intent.IntentName}";
    scenario_family = {renderFamilyValue intent.ScenarioFamily};
    target_name = {renderOptionString intent.TargetName};
    target_kind = {renderTargetKindValue intent.TargetKind};
    service_class = {renderOptionString intent.ServiceClass};
    event_month = {renderOptionString (intent.EventDate |> Option.map (fun value -> value.ToString("MMMM", CultureInfo.InvariantCulture)))};
    event_day = {renderOptionNat (intent.EventDate |> Option.map (fun value -> value.Day))};
    event_year = {renderOptionNat (intent.EventDate |> Option.map (fun value -> value.Year))};
    start_hour = {renderOptionNat intent.StartHour};
    end_hour = {renderOptionNat intent.EndHour};
    timezone = {renderOptionString intent.Timezone};
    primary_device_count = {renderOptionNat intent.PrimaryDeviceCount};
    auxiliary_endpoint_count = {renderOptionNat intent.AuxiliaryEndpointCount};
    max_latency_ms = {renderOptionNat intent.MaxLatencyMs};
    reporting_interval_minutes = {renderOptionNat intent.ReportingIntervalMinutes};
    immediate_degradation_alerts = {(string intent.ImmediateDegradationAlerts).ToLowerInvariant()};
    safety_policy_declared = {(string intent.SafetyPolicyDeclared).ToLowerInvariant()};
    preserve_emergency_traffic = {(string intent.PreserveEmergencyTraffic).ToLowerInvariant()};
    request_public_safety_preemption = {(string intent.RequestPublicSafetyPreemption).ToLowerInvariant()} }}
"""

    let private unescapeFStarString (value: string) =
        value.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\")

    let private parseQuotedString (value: string) =
        let trimmed = value.Trim()
        if trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2 then
            Some(unescapeFStarString (trimmed.Substring(1, trimmed.Length - 2)))
        else
            None

    let private parseStringOption (fieldName: string) (value: string) : Result<string option, ProcessingDiagnostic> =
        let trimmed = value.Trim()
        if trimmed = "None" then
            Ok None
        else if trimmed.StartsWith("Some ") then
            match parseQuotedString (trimmed.Substring(5)) with
            | Some parsed -> Ok(Some parsed)
            | None ->
                Error(
                    diagnostic
                        "INVALID_FSTAR_OPTION"
                        $"Field '{fieldName}' must be None or Some \"...\"."
                        (Some value)
                )
        else
            Error(
                diagnostic
                    "INVALID_FSTAR_OPTION"
                    $"Field '{fieldName}' must be None or Some \"...\"."
                    (Some value)
            )

    let private parseRequiredString (fieldName: string) (value: string) : Result<string, ProcessingDiagnostic> =
        match parseQuotedString value with
        | Some parsed when not (String.IsNullOrWhiteSpace parsed) -> Ok parsed
        | _ ->
            Error(
                diagnostic
                    "INVALID_FSTAR_STRING"
                    $"Field '{fieldName}' must be a quoted string."
                    (Some value)
            )

    let private parseNatOption (fieldName: string) (value: string) : Result<int option, ProcessingDiagnostic> =
        let trimmed = value.Trim()
        if trimmed = "None" then
            Ok None
        else if trimmed.StartsWith("Some ") then
            match Int32.TryParse(trimmed.Substring(5).Trim()) with
            | true, parsed when parsed >= 0 -> Ok(Some parsed)
            | _ ->
                Error(
                    diagnostic
                        "INVALID_FSTAR_NAT_OPTION"
                        $"Field '{fieldName}' must be None or Some <nat>."
                        (Some value)
                )
        else
            Error(
                diagnostic
                    "INVALID_FSTAR_NAT_OPTION"
                    $"Field '{fieldName}' must be None or Some <nat>."
                    (Some value)
            )

    let private parseBool (fieldName: string) (value: string) : Result<bool, ProcessingDiagnostic> =
        match value.Trim().ToLowerInvariant() with
        | "true" -> Ok true
        | "false" -> Ok false
        | _ ->
            Error(
                diagnostic
                    "INVALID_FSTAR_BOOL"
                    $"Field '{fieldName}' must be true or false."
                    (Some value)
            )

    let private parseEnum fieldName parser value =
        match parser value with
        | Some parsed -> Ok parsed
        | None ->
            Error(
                diagnostic
                    "INVALID_FSTAR_ENUM"
                    $"Field '{fieldName}' used an unsupported enum value."
                    (Some value)
            )

    let tryParseCandidateModule moduleText =
        let moduleMatch = Regex.Match(moduleText, @"(?m)^\s*module\s+(?<name>[A-Za-z0-9_]+)\s*$")
        let bindingMatch =
            Regex.Match(
                moduleText,
                @"(?s)let\s+(?<binding>[A-Za-z0-9_]+)\s*:\s*raw_tm_intent\s*=\s*\{(?<body>.*?)\}",
                RegexOptions.Singleline
            )

        let parseError code message details =
            Error [ diagnostic code message details ]

        if not moduleMatch.Success then
            parseError "MISSING_FSTAR_MODULE" "The F* output is missing a module declaration." None
        else if not bindingMatch.Success then
            parseError
                "MISSING_FSTAR_RECORD"
                "The F* output must declare `let <name> : raw_tm_intent = { ... }`."
                None
        else
            let body = bindingMatch.Groups["body"].Value

            let fieldMap =
                body.Split([| ';'; '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                |> Array.map (fun item -> item.Trim())
                |> Array.filter (String.IsNullOrWhiteSpace >> not)
                |> Array.choose (fun item ->
                    match item.Split('=', 2, StringSplitOptions.TrimEntries) with
                    | [| key; value |] -> Some(key, value)
                    | _ -> None)
                |> Map.ofArray

            let requiredFields =
                [ "intent_name"
                  "scenario_family"
                  "target_name"
                  "target_kind"
                  "service_class"
                  "event_month"
                  "event_day"
                  "event_year"
                  "start_hour"
                  "end_hour"
                  "timezone"
                  "primary_device_count"
                  "auxiliary_endpoint_count"
                  "max_latency_ms"
                  "reporting_interval_minutes"
                  "immediate_degradation_alerts"
                  "safety_policy_declared"
                  "preserve_emergency_traffic"
                  "request_public_safety_preemption" ]

            let missing =
                requiredFields
                |> List.filter (fun fieldName -> not (fieldMap.ContainsKey fieldName))
                |> List.map (fun fieldName ->
                    diagnostic
                        "MISSING_FSTAR_FIELD"
                        $"The F* output is missing required field '{fieldName}'."
                        None)

            if not missing.IsEmpty then
                Error missing
            else
                let targetKindResult =
                    if fieldMap["target_kind"].Trim() = "None" then
                        Ok None
                    else if fieldMap["target_kind"].Trim().StartsWith("Some ") then
                        parseEnum
                            "target_kind"
                            parseTargetKind
                            (fieldMap["target_kind"].Trim().Substring(5).Trim())
                        |> Result.map Some
                    else
                        Error(
                            diagnostic
                                "INVALID_FSTAR_ENUM"
                                "Field 'target_kind' must be None or Some VenueTarget/FacilityTarget."
                                (Some fieldMap["target_kind"])
                        )

                let parsedFields =
                    [ parseRequiredString "intent_name" fieldMap["intent_name"] |> Result.map (fun value -> box value)
                      parseEnum "scenario_family" parseFamily fieldMap["scenario_family"] |> Result.map (fun value -> box value)
                      parseStringOption "target_name" fieldMap["target_name"] |> Result.map (fun value -> box value)
                      targetKindResult |> Result.map (fun value -> box value)
                      parseStringOption "service_class" fieldMap["service_class"] |> Result.map (fun value -> box value)
                      parseStringOption "event_month" fieldMap["event_month"] |> Result.map (fun value -> box value)
                      parseNatOption "event_day" fieldMap["event_day"] |> Result.map (fun value -> box value)
                      parseNatOption "event_year" fieldMap["event_year"] |> Result.map (fun value -> box value)
                      parseNatOption "start_hour" fieldMap["start_hour"] |> Result.map (fun value -> box value)
                      parseNatOption "end_hour" fieldMap["end_hour"] |> Result.map (fun value -> box value)
                      parseStringOption "timezone" fieldMap["timezone"] |> Result.map (fun value -> box value)
                      parseNatOption "primary_device_count" fieldMap["primary_device_count"] |> Result.map (fun value -> box value)
                      parseNatOption "auxiliary_endpoint_count" fieldMap["auxiliary_endpoint_count"] |> Result.map (fun value -> box value)
                      parseNatOption "max_latency_ms" fieldMap["max_latency_ms"] |> Result.map (fun value -> box value)
                      parseNatOption "reporting_interval_minutes" fieldMap["reporting_interval_minutes"] |> Result.map (fun value -> box value)
                      parseBool "immediate_degradation_alerts" fieldMap["immediate_degradation_alerts"] |> Result.map (fun value -> box value)
                      parseBool "safety_policy_declared" fieldMap["safety_policy_declared"] |> Result.map (fun value -> box value)
                      parseBool "preserve_emergency_traffic" fieldMap["preserve_emergency_traffic"] |> Result.map (fun value -> box value)
                      parseBool "request_public_safety_preemption" fieldMap["request_public_safety_preemption"] |> Result.map (fun value -> box value) ]

                let fieldErrors =
                    parsedFields
                    |> List.choose (function
                        | Ok _ -> None
                        | Error diag -> Some diag)

                if not fieldErrors.IsEmpty then
                    Error fieldErrors
                else
                    let values =
                        parsedFields
                        |> List.choose (function
                            | Ok value -> Some value
                            | Error _ -> None)

                    let intentName = values[0] :?> string
                    let scenarioFamily = values[1] :?> IntentScenarioFamily
                    let targetName = values[2] :?> string option
                    let targetKind = values[3] :?> IntentTargetKind option
                    let serviceClass = values[4] :?> string option
                    let eventMonth = values[5] :?> string option
                    let eventDay = values[6] :?> int option
                    let eventYear = values[7] :?> int option
                    let startHour = values[8] :?> int option
                    let endHour = values[9] :?> int option
                    let timezone = values[10] :?> string option
                    let primaryDeviceCount = values[11] :?> int option
                    let auxiliaryEndpointCount = values[12] :?> int option
                    let maxLatencyMs = values[13] :?> int option
                    let reportingIntervalMinutes = values[14] :?> int option
                    let immediateDegradationAlerts = values[15] :?> bool
                    let safetyPolicyDeclared = values[16] :?> bool
                    let preserveEmergencyTraffic = values[17] :?> bool
                    let requestPublicSafetyPreemption = values[18] :?> bool

                    let eventDate =
                        match eventMonth, eventDay, eventYear with
                        | Some month, Some dayValue, Some yearValue ->
                            let text = $"{month} {dayValue}, {yearValue}"
                            match DateOnly.TryParse(text, CultureInfo.InvariantCulture) with
                            | true, parsed -> Some parsed
                            | _ -> None
                        | _ -> None

                    Ok
                        { ModuleName = moduleMatch.Groups["name"].Value
                          IntentBindingName = bindingMatch.Groups["binding"].Value
                          Intent =
                            { IntentName = intentName
                              ScenarioFamily = scenarioFamily
                              TargetName = targetName
                              TargetKind = targetKind
                              ServiceClass = serviceClass
                              EventDate = eventDate
                              StartHour = startHour
                              EndHour = endHour
                              Timezone = timezone
                              PrimaryDeviceCount = primaryDeviceCount
                              AuxiliaryEndpointCount = auxiliaryEndpointCount
                              MaxLatencyMs = maxLatencyMs
                              ReportingIntervalMinutes = reportingIntervalMinutes
                              ImmediateDegradationAlerts = immediateDegradationAlerts
                              SafetyPolicyDeclared = safetyPolicyDeclared
                              PreserveEmergencyTraffic = preserveEmergencyTraffic
                              RequestPublicSafetyPreemption = requestPublicSafetyPreemption
                              SourceText = None }
                          SourceText = moduleText }

    let private fstarLibraryDir () =
        Path.Combine(repoRoot (), "Tmf921.IntentManagement.Api", "FStar")

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

    let private checkerVersion () =
        let exitCode, stdout, stderr = runProcess "fstar.exe" "--version"
        if exitCode = 0 then
            Some(stdout.Trim())
        else if not (String.IsNullOrWhiteSpace stderr) then
            Some(stderr.Trim())
        else
            None

    let private runFStarModule includeDirs modulePath moduleText =
        let includes =
            includeDirs
            |> List.map (fun dir -> $"--include \"{dir}\"")
            |> String.concat " "

        let arguments = $"{includes} \"{modulePath}\""
        let exitCode, stdout, stderr = runProcess "fstar.exe" arguments

        { Success = exitCode = 0
          CheckerVersion = checkerVersion ()
          ModuleText = moduleText
          Stdout = stdout
          Stderr = stderr
          Diagnostics =
            if exitCode = 0 then
                []
            else
                [ diagnostic
                    "FSTAR_CHECK_FAILED"
                    "The generated F* module did not type-check."
                    (Some stderr) ] }

    let private buildTmWitnessModule candidate =
        let moduleName = $"{candidate.ModuleName}_TmWitness"
        let binding = candidate.IntentBindingName
        let text =
            $"""module {moduleName}

open TmForumTr292CommonCore
open {candidate.ModuleName}

let measurable_witness : measurable_intent {binding} =
  mk_measurable {binding}

let quantity_witness : quantity_checked_intent {binding} =
  mk_quantity_checked {binding}
"""

        moduleName, text

    let private buildProviderWitnessModule candidate =
        let moduleName = $"{candidate.ModuleName}_ProviderWitness"
        let binding = candidate.IntentBindingName
        let text =
            $"""module {moduleName}

open ProviderIntentAdmission
open {candidate.ModuleName}

let window_witness : window_checked_intent {binding} =
  mk_window_checked {binding}

let selected_profile : profile =
  resolve_profile {binding}

let profiled_witness : profiled_intent selected_profile {binding} =
  mk_profiled selected_profile {binding}

let capacity_witness : capacity_checked_intent selected_profile {binding} =
  mk_capacity_checked selected_profile {binding}

let latency_witness : latency_checked_intent selected_profile {binding} =
  mk_latency_checked selected_profile {binding}

let policy_witness : policy_checked_intent selected_profile {binding} =
  mk_policy_checked selected_profile {binding}

let provider_witness : provider_checked_intent selected_profile {binding} =
  mk_provider_checked selected_profile {binding}

let admission_token_for_demo : admission_token selected_profile =
  issue_admission_token selected_profile {binding} provider_witness
"""

        moduleName, text

    let runAdmissionChecks outputDir candidate =
        Directory.CreateDirectory(outputDir) |> ignore
        let includeDirs = [ fstarLibraryDir (); outputDir ]

        let candidatePath = Path.Combine(outputDir, $"{candidate.ModuleName}.fst")
        File.WriteAllText(candidatePath, candidate.SourceText, Encoding.UTF8)
        let candidateCheck = runFStarModule includeDirs candidatePath candidate.SourceText

        let tmModuleName, tmModuleText = buildTmWitnessModule candidate
        let tmModulePath = Path.Combine(outputDir, $"{tmModuleName}.fst")

        let tmWitness =
            if candidateCheck.Success then
                File.WriteAllText(tmModulePath, tmModuleText, Encoding.UTF8)
                Some(runFStarModule includeDirs tmModulePath tmModuleText)
            else
                None

        let providerModuleName, providerModuleText = buildProviderWitnessModule candidate
        let providerModulePath = Path.Combine(outputDir, $"{providerModuleName}.fst")

        let providerWitness =
            match tmWitness with
            | Some tmResult when tmResult.Success ->
                File.WriteAllText(providerModulePath, providerModuleText, Encoding.UTF8)
                Some(runFStarModule includeDirs providerModulePath providerModuleText)
            | _ -> None

        let firstFailedWitness =
            if not candidateCheck.Success then
                Some "candidate_module_compile"
            else
                match tmWitness with
                | Some tmResult when not tmResult.Success -> tmFailedWitness candidate.Intent
                | Some _ ->
                    match providerWitness with
                    | Some providerResult when not providerResult.Success -> providerFailedWitness candidate.Intent
                    | Some _ -> None
                    | None -> None
                | None -> Some "measurable_intent"

        let tmWitnessStatus =
            match tmWitness with
            | Some result when result.Success -> "passed"
            | Some _ -> "failed"
            | None -> "failed"

        let providerWitnessStatus =
            match providerWitness with
            | Some result when result.Success -> "passed"
            | Some _ -> "failed"
            | None -> "not_run"

        let admissionOutcome =
            match tmWitness, providerWitness with
            | Some tmResult, Some providerResult when tmResult.Success && providerResult.Success -> "provider_admitted"
            | Some tmResult, _ when tmResult.Success -> "tm_validated_only"
            | _ -> "not_admitted"

        { CandidateModuleName = candidate.ModuleName
          CandidateCheck = candidateCheck
          TmWitness = tmWitness
          ProviderWitness = providerWitness
          SelectedProfile =
            match profileForIntent candidate.Intent with
            | UnsupportedProfile -> None
            | profile -> Some(profileName profile)
          FirstFailedWitness = firstFailedWitness
          TmWitnessStatus = tmWitnessStatus
          ProviderWitnessStatus = providerWitnessStatus
          AdmissionOutcome = admissionOutcome }
