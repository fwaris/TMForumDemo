namespace Tmf921.IntentManagement.Api

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
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
      UseScenarioFixtures: bool }

type RawIntentGenerationResult =
    { Envelope: RawIntentParseEnvelope option
      Metadata: LlmParseMetadata
      PromptText: string option
      RawResponseText: string option
      Diagnostics: ProcessingDiagnostic list }

type IRawIntentGenerator =
    abstract member GenerateSemanticCoreAsync:
        context: RawIntentGenerationContext * text: string * cancellationToken: CancellationToken ->
            Task<RawIntentGenerationResult>

[<CLIMutable>]
type ScenarioRawIntentFixture =
    { ScenarioId: string
      Model: string
      PromptVersion: string
      PromptText: string
      ResponseText: string
      Envelope: RawIntentParseEnvelope }

type SchemaValidationResult =
    { Accepted: bool
      Issues: ProcessingDiagnostic list
      Report: JsonElement }

module RawIntentGenerationContext =
    let Live =
        { ScenarioId = None
          UseScenarioFixtures = false }

    let ForScenario scenarioId =
        { ScenarioId = Some scenarioId
          UseScenarioFixtures = true }

module IntentLlmDefaults =
    let value =
        { Model = "gpt-5.4"
          MaxAttempts = 3
          Temperature = 0.0f
          TimeoutSeconds = 30
          UseScenarioFixtures = true }

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

type RawIntentGenerator(chatClient: IChatClient, options: IntentLlmOptions) =
    let promptVersion = "2026-04-19.raw-intent.v1"

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
- Convert natural-language telecom-management intent text into ontology-aligned semantic content.
- Return only structured data that conforms to the requested schema.

Rules:
- Extract semantics from the user's text without inventing targets, time windows, quantities, or policy facts.
- Do not fix contradictions. Preserve explicit bad-but-stated semantics, such as reversed time windows.
- Do not perform provider-policy reasoning. This step only normalizes raw intent semantics.
- If the text is too vague to identify at least one target and one measurable expectation without guessing, return status = "clarification_required".
- The model owns only: intentName, description, targets, expectations, context, priority.
- Never output profile, provenance, checker, or policy fields.
- Prefer compact normalized values over prose when possible.

Few-shot guidance:
1. Valid paraphrase:
Input: "Set up premium 5G broadcast coverage for Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit for 200 production devices, keep uplink latency under 20 ms, send hourly compliance updates and immediate alerts if service quality degrades, and do not impact emergency-service traffic."
Behavior: return status = "parsed" with a target for Detroit Stadium, measurable expectations, and the explicit protected-traffic requirement carried as an expectation/condition rather than omitted.

2. Vague request:
Input: "Make the event network really good and fast for the broadcast."
Behavior: return status = "clarification_required" with issues explaining that the target and measurable expectations are missing.

3. Reversed window:
Input: "Provide premium 5G broadcast service at Detroit Stadium on April 25, 2026 from 22:00 to 18:00 America/Detroit with latency under 20 ms and hourly reports."
Behavior: return status = "parsed". Preserve the reversed window semantics in the description or expectation content instead of correcting it.

4. Multi-constraint broadcast:
Input: "Support 90 production devices at Metro Arena on April 25, 2026 from 18:00 to 22:00 America/Detroit, keep uplink latency under 30 ms, send hourly updates and immediate alerts, and do not impact emergency-service traffic."
Behavior: return status = "parsed" with the venue target and multiple expectations.

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

        $"""Normalize the following natural-language input into ontology semantic content.

Natural-language intent:
\"\"\"{text}\"\"\"
{repairSection}

Reminder:
- If you cannot produce a target and at least one expectation without guessing, return status = "clarification_required".
- Do not emit provider or provenance fields.
- Do not explain outside the structured response."""

    let trySerializeRawRepresentation (value: obj) =
        if isNull value then
            None
        else
            try
                Some(JsonSerializer.Serialize(value, serializerOptions))
            with _ ->
                None

    let normalizeOptionalString (value: string option) =
        value
        |> Option.bind (fun text ->
            let trimmed = text.Trim()
            if String.IsNullOrWhiteSpace trimmed then None else Some trimmed)

    let normalizeConditionClause (value: RawIntentConditionClause) =
        { Kind = normalizeOptionalString value.Kind
          Subject = normalizeOptionalString value.Subject
          Operator = normalizeOptionalString value.Operator
          Value = normalizeOptionalString value.Value }

    let normalizeCondition (value: RawIntentCondition) =
        let children =
            if isNull (box value.Children) then
                []
            else
                value.Children

        { Kind = normalizeOptionalString value.Kind
          Subject = normalizeOptionalString value.Subject
          Operator = normalizeOptionalString value.Operator
          Value = normalizeOptionalString value.Value
          Children = children |> List.map normalizeConditionClause }

    let normalizeTarget (value: RawIntentTarget) =
        { Id = normalizeOptionalString value.Id
          TargetType = normalizeOptionalString value.TargetType
          Name = normalizeOptionalString value.Name }

    let normalizeQuantity (value: RawIntentQuantity) =
        { Value = normalizeOptionalString value.Value
          Unit = normalizeOptionalString value.Unit }

    let normalizeFunctionApplication (value: RawIntentFunctionApplication) =
        let arguments =
            if isNull (box value.Arguments) then
                []
            else
                value.Arguments

        { Name = normalizeOptionalString value.Name
          Arguments =
            arguments
            |> List.map (fun argument -> argument.Trim())
            |> List.filter (String.IsNullOrWhiteSpace >> not) }

    let normalizeExpectation (value: RawIntentExpectation) =
        { Kind = normalizeOptionalString value.Kind
          Subject = normalizeOptionalString value.Subject
          Description = normalizeOptionalString value.Description
          Condition = value.Condition |> Option.map normalizeCondition
          Quantity = value.Quantity |> Option.map normalizeQuantity
          FunctionApplication = value.FunctionApplication |> Option.map normalizeFunctionApplication }

    let normalizeEnvelope (envelope: RawIntentParseEnvelope) =
        let normalizedCore =
            envelope.SemanticCore
            |> Option.map (fun core ->
                let targets =
                    if isNull (box core.Targets) then
                        []
                    else
                        core.Targets

                let expectations =
                    if isNull (box core.Expectations) then
                        []
                    else
                        core.Expectations

                { IntentName = normalizeOptionalString core.IntentName
                  Description = normalizeOptionalString core.Description
                  Targets = targets |> List.map normalizeTarget
                  Expectations = expectations |> List.map normalizeExpectation
                  Context = normalizeOptionalString core.Context
                  Priority = normalizeOptionalString core.Priority })

        let normalizedIssues =
            (if isNull (box envelope.Issues) then [] else envelope.Issues)
            |> List.map (fun issue ->
                { issue with
                    Code = issue.Code.Trim()
                    Message = issue.Message.Trim()
                    Details = normalizeOptionalString issue.Details })

        { Status =
            if isNull envelope.Status then
                ""
            else
                envelope.Status.Trim().ToLowerInvariant()
          SemanticCore = normalizedCore
          Issues = normalizedIssues }

    let validateEnvelope (envelope: RawIntentParseEnvelope) =
        let normalized = normalizeEnvelope envelope
        let schemaValidation =
            JsonSerializer.SerializeToElement(normalized, serializerOptions)
            |> RawIntentContracts.validateParseEnvelope

        let semanticIssues =
            match normalized.Status, normalized.SemanticCore with
            | "parsed", None ->
                [ diagnostic "SCHEMA_MISMATCH" "The model reported a parsed result without semanticCore content." None ]
            | "parsed", Some semanticCore ->
                [ if semanticCore.IntentName |> Option.isNone then
                      yield diagnostic "MISSING_INTENT_NAME" "The parsed intent is missing intentName." None
                  if semanticCore.Targets.IsEmpty then
                      yield diagnostic "MISSING_TARGETS" "The parsed intent is missing targets." None
                  if semanticCore.Expectations.IsEmpty then
                      yield diagnostic "MISSING_EXPECTATIONS" "The parsed intent is missing expectations." None ]
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
        options.MaxOutputTokens <- Nullable 3000
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
        member _.GenerateSemanticCoreAsync(context, text, cancellationToken) =
            task {
                let fixtureResult =
                    match context.ScenarioId with
                    | Some scenarioId when effectiveOptions.UseScenarioFixtures && context.UseScenarioFixtures ->
                        match RawIntentScenarioFixtures.tryRead scenarioId with
                        | Some fixture ->
                            let normalizedEnvelope = normalizeEnvelope fixture.Envelope
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
                    let mutable priorIssues = []
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
                                    chatClient.GetResponseAsync<RawIntentParseEnvelope>(
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

                                let responseId =
                                    response.ResponseId |> Option.ofObj

                                let mutable parsed = Unchecked.defaultof<RawIntentParseEnvelope>

                                if response.TryGetResult(&parsed) then
                                    let normalizedEnvelope, validationIssues, _ = validateEnvelope parsed

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
