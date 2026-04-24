namespace Tmf921.IntentManagement.Api

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.AI

[<CLIMutable>]
type IntentLlmOptions =
    { Model: string
      MaxAttempts: int
      Temperature: float32
      TimeoutSeconds: int
      UseScenarioFixtures: bool }

type RawIntentGenerationContext =
    { ScenarioId: string option
      UseScenarioFixtures: bool
      RepairIssues: ProcessingDiagnostic list }

type RawIntentGenerationResult =
    { Envelope: FStarIntentModuleEnvelope option
      Metadata: LlmParseMetadata
      PromptText: string option
      RawResponseText: string option
      Diagnostics: ProcessingDiagnostic list }

type IRawIntentGenerator =
    abstract member GenerateIntentModuleAsync:
        context: RawIntentGenerationContext * text: string * cancellationToken: CancellationToken ->
            Task<RawIntentGenerationResult>

[<CLIMutable>]
type ScenarioRawIntentFixture =
    { ScenarioId: string
      Model: string
      PromptVersion: string
      PromptText: string
      ResponseText: string
      Envelope: FStarIntentModuleEnvelope }

type SchemaValidationResult =
    { Accepted: bool
      Issues: ProcessingDiagnostic list
      Report: JsonElement }

module RawIntentGenerationContext =
    let Live =
        { ScenarioId = None
          UseScenarioFixtures = false
          RepairIssues = [] }

    let ForScenario scenarioId =
        { ScenarioId = Some scenarioId
          UseScenarioFixtures = true
          RepairIssues = [] }

module IntentLlmDefaults =
    let value =
        { Model = "gpt-5.4"
          MaxAttempts = 3
          Temperature = 0.0f
          TimeoutSeconds = 30
          UseScenarioFixtures = false }

module RawIntentContracts =
    let private schemasDir () =
        Path.Combine(repoRoot (), "Tmf921.IntentManagement.Api", "Schemas")

    let private parseEnvelopeSchema =
        lazy
            let path = Path.Combine(schemasDir (), "RawIntentParseEnvelope.schema.json")
            Json.Schema.JsonSchema.FromText(File.ReadAllText(path))

    let private ontologyRawIntentSchema =
        lazy
            let path = Path.Combine(schemasDir (), "OntologyRawIntent.schema.json")
            Json.Schema.JsonSchema.FromText(File.ReadAllText(path))

    let rec private collectIssues (element: JsonElement) =
        let nested =
            match element.ValueKind with
            | JsonValueKind.Object ->
                let mutable detailsProperty = Unchecked.defaultof<JsonElement>
                let hasDetails = element.TryGetProperty("details", &detailsProperty)

                if hasDetails && detailsProperty.ValueKind = JsonValueKind.Array then
                    detailsProperty.EnumerateArray()
                    |> Seq.toList
                    |> List.collect collectIssues
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
                    |> List.map (fun entry -> $"{entry.Name}: {entry.Value} (instance: {instanceLocation})")
                else
                    []
            | _ -> []

        local @ nested

    let private validate (schema: Lazy<Json.Schema.JsonSchema>) (element: JsonElement) =
        let options = Json.Schema.EvaluationOptions()
        options.OutputFormat <- Json.Schema.OutputFormat.List
        options.RequireFormatValidation <- true

        let result = schema.Value.Evaluate(element, options)
        let report = JsonSerializer.SerializeToElement(result.ToList(), serializerOptions)

        let issues =
            if result.IsValid then
                []
            else
                collectIssues report
                |> List.distinct
                |> List.map (fun message ->
                    { Code = "SCHEMA_MISMATCH"
                      Message = $"The structured output failed schema validation: {message}"
                      Details = None })

        { Accepted = result.IsValid
          Issues = issues
          Report = report }

    let validateParseEnvelope (element: JsonElement) =
        validate parseEnvelopeSchema element

    let validateOntologyRawIntent (element: JsonElement) =
        validate ontologyRawIntentSchema element

module RawIntentScenarioFixtures =
    let private fixturesDir () =
        Path.Combine(repoRoot (), "Tmf921.IntentManagement.Api", "DemoFixtures", "RawIntentLlm")

    let private fixturePath (scenarioId: string) =
        Path.Combine(fixturesDir (), $"{scenarioId}.json")

    let tryRead scenarioId =
        let path = fixturePath scenarioId

        if File.Exists path then
            let fixture =
                JsonSerializer.Deserialize<ScenarioRawIntentFixture>(File.ReadAllText(path, Encoding.UTF8), serializerOptions)

            Some fixture
        else
            None

    let write (fixture: ScenarioRawIntentFixture) =
        Directory.CreateDirectory(fixturesDir ()) |> ignore
        let path = fixturePath fixture.ScenarioId
        File.WriteAllText(path, JsonSerializer.Serialize(fixture, serializerOptions), Encoding.UTF8)
        path

module RawIntentGenerationValidation =
    type SimulatedAttemptSequenceResult =
        { Envelope: FStarIntentModuleEnvelope option
          SelectedOutcome: string
          Attempts: LlmParseAttempt list
          Diagnostics: ProcessingDiagnostic list }

    let private diagnostic code message details =
        { Code = code
          Message = message
          Details = details }

    let private normalizeOptionalString (value: string option) =
        value
        |> Option.bind (fun text ->
            let trimmed = text.Trim()
            if String.IsNullOrWhiteSpace trimmed then None else Some trimmed)

    let normalizeEnvelope (envelope: FStarIntentModuleEnvelope) =
        { Status =
            if isNull envelope.Status then
                ""
            else
                envelope.Status.Trim().ToLowerInvariant()
          ModuleText =
            envelope.ModuleText
            |> normalizeOptionalString
            |> Option.map (fun value -> value.Replace("\r\n", "\n"))
          Issues =
            (if isNull (box envelope.Issues) then [] else envelope.Issues)
            |> List.map (fun issue ->
                { issue with
                    Code = issue.Code.Trim()
                    Message = issue.Message.Trim()
                    Details = normalizeOptionalString issue.Details }) }

    let validateEnvelope (envelope: FStarIntentModuleEnvelope) =
        let normalized = normalizeEnvelope envelope
        let schemaValidation =
            JsonSerializer.SerializeToElement(normalized, serializerOptions)
            |> RawIntentContracts.validateParseEnvelope

        let semanticIssues =
            match normalized.Status, normalized.ModuleText with
            | "parsed", None ->
                [ diagnostic "MISSING_MODULE_TEXT" "The model reported a parsed result without moduleText." None ]
            | "parsed", Some moduleText ->
                match IntentAdmission.tryParseCandidateModule moduleText with
                | Ok _ -> []
                | Error issues -> issues
            | "clarification_required", _ ->
                if normalized.Issues.IsEmpty then
                    [ diagnostic
                        "CLARIFICATION_REQUIRED"
                        "The model requested clarification but did not explain which semantic fields were missing."
                        None ]
                else
                    []
            | other, _ ->
                [ diagnostic
                    "SCHEMA_MISMATCH"
                    $"The model returned an unsupported status '{other}'."
                    None ]

        normalized, (schemaValidation.Issues @ semanticIssues), schemaValidation.Report

    let simulateAttemptSequence (envelopes: FStarIntentModuleEnvelope list) =
        let attempts = ResizeArray<LlmParseAttempt>()
        let mutable selectedEnvelope = None
        let mutable selectedOutcome = "exhausted"
        let mutable finalDiagnostics = []

        for index = 0 to envelopes.Length - 1 do
            if selectedEnvelope.IsNone && finalDiagnostics.IsEmpty then
                let normalizedEnvelope, validationIssues, _ = validateEnvelope envelopes[index]

                attempts.Add
                    { Attempt = index + 1
                      Source = "simulation"
                      Outcome =
                        if validationIssues.IsEmpty then
                            normalizedEnvelope.Status
                        else
                            "repair_required"
                      ResponseId = None
                      FinishReason = None
                      Issues = validationIssues }

                if validationIssues.IsEmpty then
                    selectedEnvelope <- Some normalizedEnvelope
                    selectedOutcome <- normalizedEnvelope.Status
                    finalDiagnostics <-
                        if normalizedEnvelope.Status = "clarification_required" then
                            normalizedEnvelope.Issues
                        else
                            []
                else if index = envelopes.Length - 1 then
                    selectedOutcome <- "repair_exhausted"
                    finalDiagnostics <- validationIssues

        { Envelope = selectedEnvelope
          SelectedOutcome = selectedOutcome
          Attempts = attempts |> Seq.toList
          Diagnostics = finalDiagnostics }

type RawIntentGenerator(chatClient: IChatClient, options: IntentLlmOptions) =
    let promptVersion = "2026-04-23.fstar-intent.v2"

    let diagnostic code message details =
        { Code = code
          Message = message
          Details = details }

    let effectiveOptions =
        { Model =
            if String.IsNullOrWhiteSpace options.Model then
                IntentLlmDefaults.value.Model
            else
                options.Model
          MaxAttempts = max 1 options.MaxAttempts
          Temperature = max 0.0f options.Temperature
          TimeoutSeconds = max 1 options.TimeoutSeconds
          UseScenarioFixtures = options.UseScenarioFixtures }

    let buildBasePrompt () =
        $"""You are a semantic normalization component for TMF921 intent admission.

Task:
- Convert natural-language telecom-management intent text into a restricted F* intent module.
- Return only structured data that conforms to the requested schema.

Output contract:
- If the request is specific enough to map into the supported admission subset, return status = "parsed" and moduleText containing exactly one F* module.
- If the request is too vague or missing core semantics, return status = "clarification_required" with issues and omit moduleText.

Allowed F* shape:
- The module must open `TmForumTr292CommonCore`.
- It must declare exactly one `let candidate_intent : raw_tm_intent = {{ ... }}` record.
- Prefer the module name `GeneratedIntent`.
- Use only these record fields:
  intent_name
  scenario_family
  target_name
  target_kind
  service_class
  event_month
  event_day
  event_year
  start_hour
  end_hour
  timezone
  primary_device_count
  auxiliary_endpoint_count
  max_latency_ms
  reporting_interval_minutes
  immediate_degradation_alerts
  safety_policy_declared
  preserve_emergency_traffic
  request_public_safety_preemption

Allowed enum values:
- scenario_family: BroadcastFamily | CriticalServiceFamily
- target_kind: Some VenueTarget | Some FacilityTarget | None

Exact field formatting rules:
- `intent_name` is a required string, so write `"..."`, never `Some "..."`.
- `target_name`, `service_class`, `timezone`, and `event_month` are optional strings, so write either `None` or `Some "..."`.
- `event_month` must be a month name string such as `Some "April"`, never `Some 4`.
- `event_day`, `event_year`, `start_hour`, `end_hour`, `primary_device_count`, `auxiliary_endpoint_count`, `max_latency_ms`, and `reporting_interval_minutes` are optional naturals, so write either `None` or `Some 25`.
- `target_kind` is an optional enum, so write `Some VenueTarget`, `Some FacilityTarget`, or `None`.
- Boolean fields must be bare `true` or `false`.

Canonical module template:
```fstar
module GeneratedIntent

open TmForumTr292CommonCore

let candidate_intent : raw_tm_intent =
  {{ intent_name = "LiveBroadcastIntent";
    scenario_family = BroadcastFamily;
    target_name = Some "Detroit Stadium";
    target_kind = Some VenueTarget;
    service_class = Some "premium-5g-broadcast";
    event_month = Some "April";
    event_day = Some 25;
    event_year = Some 2026;
    start_hour = Some 18;
    end_hour = Some 22;
    timezone = Some "America/Detroit";
    primary_device_count = Some 200;
    auxiliary_endpoint_count = None;
    max_latency_ms = Some 20;
    reporting_interval_minutes = Some 60;
    immediate_degradation_alerts = true;
    safety_policy_declared = true;
    preserve_emergency_traffic = true;
    request_public_safety_preemption = false }}
```

Common syntax mistakes to avoid:
- Wrong: `intent_name = Some "LiveBroadcastIntent"`
- Right: `intent_name = "LiveBroadcastIntent"`
- Wrong: `event_month = Some 4`
- Right: `event_month = Some "April"`

Rules:
- Extract semantics from the user's text without inventing targets, dates, times, quantities, or policy facts.
- Preserve explicit bad-but-stated semantics, such as reversed time windows.
- Do not perform provider-admission reasoning.
- For broadcast requests, set `intent_name = "LiveBroadcastIntent"`.
- For critical-service requests, set `intent_name = "CriticalServiceIntent"`.
- For broadcast requests, use service_class = Some "premium-5g-broadcast" when the text clearly indicates premium 5G broadcast service.
- For critical-service requests, use service_class = Some "ultra-reliable-5g-clinical" when the text clearly indicates telemedicine, clinical, or critical-care service.
- Set safety_policy_declared = true only if the text explicitly states either protected-traffic preservation or public-safety preemption.
- For BroadcastFamily, auxiliary_endpoint_count should be None unless the text explicitly includes a second endpoint quantity.
- For CriticalServiceFamily, auxiliary_endpoint_count should be Some <nat> only when the text explicitly includes auxiliary endpoints.
- If the text is too vague to identify a target, service class, and measurable expectations without guessing, return clarification_required.

Few-shot guidance:
1. Accepted broadcast:
Input: "Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
Behavior: return parsed with scenario_family = BroadcastFamily, target_name = Some "Detroit Stadium", target_kind = Some VenueTarget, primary_device_count = Some 200, reporting_interval_minutes = Some 60, immediate_degradation_alerts = true, safety_policy_declared = true, preserve_emergency_traffic = true, request_public_safety_preemption = false.

2. Accepted critical service:
Input: "Provide an ultra-reliable 5G clinical service for telemedicine and critical care operations at Mayo Clinic on April 25, 2026 from 08:00 to 20:00 America/Detroit. Support up to 80 critical devices and 200 auxiliary endpoints. Maintain end-to-end latency below 10 ms. Send compliance updates every 5 minutes and immediate alerts if service quality degrades. Do not impact emergency-service traffic."
Behavior: return parsed with scenario_family = CriticalServiceFamily, target_name = Some "Mayo Clinic", target_kind = Some FacilityTarget, primary_device_count = Some 80, auxiliary_endpoint_count = Some 200, max_latency_ms = Some 10, reporting_interval_minutes = Some 5.

3. Reversed window:
Input: "Provide premium 5G broadcast service at Detroit Stadium on April 25, 2026 from 22:00 to 18:00 America/Detroit with latency under 20 ms and hourly reports."
Behavior: return parsed and preserve start_hour = Some 22 and end_hour = Some 18 without correcting them.

4. Vague request:
Input: "Make the event network really good and fast for the broadcast."
Behavior: return clarification_required with issues describing the missing target and measurable expectations.

Current prompt version: {promptVersion}"""

    let buildUserPrompt (text: string) (repairIssues: ProcessingDiagnostic list) =
        let repairSection =
            match repairIssues with
            | [] -> ""
            | xs ->
                let lines =
                    xs
                    |> List.map (fun issue ->
                        match issue.Details with
                        | Some details when not (String.IsNullOrWhiteSpace details) ->
                            $"- {issue.Code}: {issue.Message} Details: {details}"
                        | _ -> $"- {issue.Code}: {issue.Message}")
                    |> String.concat "\n"

                $"""

Server validation failures from the previous attempt:
{lines}

Repair only those failures while preserving the user's stated semantics."""

        $"""Normalize the following natural-language input into a restricted F* intent module.

Natural-language intent:
\"\"\"{text}\"\"\"
{repairSection}

Reminder:
- Return only the structured response.
- When parsed, moduleText must contain exactly one F* module using the allowed record shape.
- Follow the canonical field formatting exactly, especially for `intent_name` and `event_month`.
- Do not emit provider witness code or additional helper functions.
- Do not invent unstated facts."""

    let trySerializeRawRepresentation (value: obj) =
        if isNull value then
            None
        else
            try
                Some(JsonSerializer.Serialize(value, serializerOptions))
            with _ ->
                None

    let maybeRefusal (rawResponseText: string option) =
        rawResponseText
        |> Option.bind (fun text ->
            let lower = text.ToLowerInvariant()

            if lower.Contains("\"refusal\"") || lower.Contains("i'm sorry") || lower.Contains("cannot assist") then
                Some(diagnostic "MODEL_REFUSAL" "The model refused to produce a structured normalization result." None)
            else
                None)

    let buildChatOptions () =
        let options = ChatOptions()
        options.ModelId <- effectiveOptions.Model
        options.Temperature <- Nullable effectiveOptions.Temperature
        options.MaxOutputTokens <- Nullable 3500
        options

    let metadata outcome usedFixture fixtureId attempts =
        { Provider = Some "OpenAI"
          Model = Some effectiveOptions.Model
          PromptVersion = Some promptVersion
          SelectedOutcome = Some outcome
          UsedFixture = usedFixture
          FixtureId = fixtureId
          Attempts = attempts }

    interface IRawIntentGenerator with
        member _.GenerateIntentModuleAsync(context, text, cancellationToken) =
            task {
                let fixtureResult =
                    match context.ScenarioId with
                    | Some scenarioId when effectiveOptions.UseScenarioFixtures && context.UseScenarioFixtures ->
                        match RawIntentScenarioFixtures.tryRead scenarioId with
                        | Some fixture ->
                            let normalizedEnvelope = RawIntentGenerationValidation.normalizeEnvelope fixture.Envelope
                            let attempt =
                                { Attempt = 1
                                  Source = "fixture"
                                  Outcome = normalizedEnvelope.Status
                                  ResponseId = None
                                  FinishReason = None
                                  Issues = normalizedEnvelope.Issues }

                            let diagnostics =
                                if normalizedEnvelope.Status = "clarification_required" then
                                    normalizedEnvelope.Issues
                                else
                                    []

                            Some
                                { Envelope = Some normalizedEnvelope
                                  Metadata = metadata normalizedEnvelope.Status true (Some scenarioId) [ attempt ]
                                  PromptText = Some fixture.PromptText
                                  RawResponseText = Some fixture.ResponseText
                                  Diagnostics = diagnostics }
                        | None -> None
                    | _ -> None

                match fixtureResult with
                | Some result ->
                    return result
                | None ->
                    let attempts = ResizeArray<LlmParseAttempt>()
                    let mutable priorIssues = context.RepairIssues
                    let mutable finalEnvelope = None
                    let mutable finalPromptText = None
                    let mutable finalResponseText = None
                    let mutable finalDiagnostics = []
                    let mutable selectedOutcome = "exhausted"

                    for attemptNumber in 1 .. effectiveOptions.MaxAttempts do
                        if finalEnvelope.IsNone && finalDiagnostics.IsEmpty then
                            let promptText =
                                [ buildBasePrompt ()
                                  buildUserPrompt text priorIssues ]
                                |> String.concat "\n\n---\n\n"

                            let messages =
                                [ ChatMessage(ChatRole.System, buildBasePrompt ())
                                  ChatMessage(ChatRole.User, buildUserPrompt text priorIssues) ]

                            finalPromptText <- Some promptText

                            try
                                use timeout = new CancellationTokenSource(TimeSpan.FromSeconds(float effectiveOptions.TimeoutSeconds))
                                use linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token)

                                let! response =
                                    chatClient.GetResponseAsync<FStarIntentModuleEnvelope>(
                                        messages,
                                        llmStructuredOutputSerializerOptions,
                                        buildChatOptions (),
                                        Nullable true,
                                        linked.Token)

                                let rawResponseText =
                                    if String.IsNullOrWhiteSpace response.Text then
                                        trySerializeRawRepresentation response.RawRepresentation
                                    else
                                        Some response.Text

                                finalResponseText <- rawResponseText

                                let finishReason =
                                    if response.FinishReason.HasValue then
                                        Some(string response.FinishReason.Value)
                                    else
                                        None

                                let responseId = response.ResponseId |> Option.ofObj
                                let mutable parsed = Unchecked.defaultof<FStarIntentModuleEnvelope>

                                if response.TryGetResult(&parsed) then
                                    let normalizedEnvelope, validationIssues, _ =
                                        RawIntentGenerationValidation.validateEnvelope parsed

                                    let attempt =
                                        { Attempt = attemptNumber
                                          Source = "live_model"
                                          Outcome =
                                            if validationIssues.IsEmpty then
                                                normalizedEnvelope.Status
                                            else
                                                "repair_required"
                                          ResponseId = responseId
                                          FinishReason = finishReason
                                          Issues = validationIssues }

                                    attempts.Add attempt

                                    if validationIssues.IsEmpty then
                                        selectedOutcome <- normalizedEnvelope.Status
                                        finalEnvelope <- Some normalizedEnvelope
                                        finalDiagnostics <-
                                            if normalizedEnvelope.Status = "clarification_required" then
                                                normalizedEnvelope.Issues
                                            else
                                                []
                                    else
                                        priorIssues <- validationIssues

                                        if attemptNumber = effectiveOptions.MaxAttempts then
                                            selectedOutcome <- "repair_exhausted"
                                            finalDiagnostics <- validationIssues
                                else
                                    let parseIssues =
                                        match maybeRefusal rawResponseText with
                                        | Some refusal -> [ refusal ]
                                        | None ->
                                            [ diagnostic
                                                "SCHEMA_MISMATCH"
                                                "The model response could not be parsed as structured output."
                                                rawResponseText ]

                                    attempts.Add
                                        { Attempt = attemptNumber
                                          Source = "live_model"
                                          Outcome = parseIssues.Head.Code.ToLowerInvariant()
                                          ResponseId = responseId
                                          FinishReason = finishReason
                                          Issues = parseIssues }

                                    priorIssues <- parseIssues

                                    if attemptNumber = effectiveOptions.MaxAttempts then
                                        selectedOutcome <- parseIssues.Head.Code.ToLowerInvariant()
                                        finalDiagnostics <- parseIssues
                            with
                            | :? OperationCanceledException as ex when not cancellationToken.IsCancellationRequested ->
                                let issues =
                                    [ diagnostic
                                        "LLM_TIMEOUT"
                                        $"The LLM request exceeded the configured timeout of {effectiveOptions.TimeoutSeconds} seconds."
                                        (Some(ex.ToString())) ]

                                attempts.Add
                                    { Attempt = attemptNumber
                                      Source = "live_model"
                                      Outcome = "timeout"
                                      ResponseId = None
                                      FinishReason = None
                                      Issues = issues }

                                priorIssues <- issues

                                if attemptNumber = effectiveOptions.MaxAttempts then
                                    selectedOutcome <- "timeout"
                                    finalDiagnostics <- issues
                            | ex ->
                                let issues =
                                    [ diagnostic
                                        "LLM_TRANSPORT_ERROR"
                                        "The LLM request failed before a structured result was returned."
                                        (Some(ex.ToString())) ]

                                attempts.Add
                                    { Attempt = attemptNumber
                                      Source = "live_model"
                                      Outcome = "transport_error"
                                      ResponseId = None
                                      FinishReason = None
                                      Issues = issues }

                                priorIssues <- issues

                                if attemptNumber = effectiveOptions.MaxAttempts then
                                    selectedOutcome <- "transport_error"
                                    finalDiagnostics <- issues

                    return
                        { Envelope = finalEnvelope
                          Metadata = metadata selectedOutcome false context.ScenarioId (attempts |> Seq.toList)
                          PromptText = finalPromptText
                          RawResponseText = finalResponseText
                          Diagnostics = finalDiagnostics }
            }
