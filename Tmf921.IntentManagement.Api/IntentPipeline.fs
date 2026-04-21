namespace Tmf921.IntentManagement.Api

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks

module IntentPipeline =
    type PipelineOutcome =
        { NormalizedExpression: IntentExpression
          ProcessingRecord: IntentProcessingRecord }

    type private CheckerRepairState =
        { Generator: IRawIntentGenerator
          GenerationContext: RawIntentGenerationContext
          RetryCount: int }

    let private checkerRepairRetryLimit = 1

    let private writeIndented path value =
        let options = JsonSerializerOptions(serializerOptions)
        options.WriteIndented <- true
        File.WriteAllText(path, JsonSerializer.Serialize(value, options))

    let private sha256 path =
        use stream = File.OpenRead(path)
        use hash = SHA256.Create()
        hash.ComputeHash(stream)
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""

    let private toArtifactReference path =
        if File.Exists(path) then
            Some { Path = path; Sha256 = Some(sha256 path) }
        else
            None

    let private diagnostics code message details =
        { Code = code
          Message = message
          Details = details }

    let private stringOrJson (node: JsonNode) =
        match node with
        | null -> None
        | :? JsonValue as value ->
            try
                let text = value.GetValue<string>()
                if String.IsNullOrWhiteSpace text then Some(node.ToJsonString()) else Some text
            with _ ->
                Some(node.ToJsonString())
        | _ -> Some(node.ToJsonString())

    let private tryGetPropertyString (obj: JsonObject) (names: string list) =
        names
        |> List.tryPick (fun name ->
            match obj[name] with
            | null -> None
            | value -> stringOrJson value)

    let rec private collectStringValues (node: JsonNode) =
        match node with
        | null -> []
        | :? JsonArray as array ->
            array
            |> Seq.toList
            |> List.collect collectStringValues
        | :? JsonObject as obj ->
            [ tryGetPropertyString obj [ "@id"; "id"; "name"; "value"; "iri"; "target" ] ]
            |> List.choose id
        | _ -> stringOrJson node |> Option.toList

    let private classifyObject (obj: JsonObject) =
        if obj.ContainsKey("@context") || obj.ContainsKey("@type") then
            InputKind.StructuredCanonical
        else if
            [ "text"; "naturalLanguage"; "utterance"; "prompt"; "value" ]
            |> List.exists (fun key ->
                match obj[key] with
                | :? JsonValue as value ->
                    try
                        let text = value.GetValue<string>()
                        not (String.IsNullOrWhiteSpace text)
                    with _ ->
                        false
                | _ -> false)
        then
            InputKind.NaturalLanguage
        else
            InputKind.StructuredNormalizable

    let classifyInput (expression: IntentExpression) =
        match expression.ExpressionValue.ValueKind with
        | JsonValueKind.String ->
            let text = expression.ExpressionValue.GetString()
            if String.IsNullOrWhiteSpace text then InputKind.Ambiguous else InputKind.NaturalLanguage
        | JsonValueKind.Object -> ensureObject expression.ExpressionValue |> classifyObject
        | JsonValueKind.Array -> InputKind.StructuredNormalizable
        | JsonValueKind.Undefined
        | JsonValueKind.Null -> InputKind.Ambiguous
        | _ -> InputKind.Ambiguous

    let private extractNaturalLanguageText (expression: IntentExpression) =
        match expression.ExpressionValue.ValueKind with
        | JsonValueKind.String -> expression.ExpressionValue.GetString() |> Option.ofObj
        | JsonValueKind.Object ->
            let obj = ensureObject expression.ExpressionValue
            tryGetPropertyString obj [ "text"; "naturalLanguage"; "utterance"; "prompt"; "value" ]
        | _ -> None

    let private targetFromString targetType value : CanonicalTarget =
        { Id = value
          TargetType = Some targetType
          Name = Some value }

    let private mkExpectation kind subject description condition quantity : CanonicalExpectation =
        { Kind = kind
          Subject = subject
          Description = description
          Condition = condition
          Quantity = quantity
          FunctionApplication = None }

    let private inferProfile (request: IntentFvo) : OntologyProfile =
        match request.IntentSpecification with
        | Some spec when not (String.IsNullOrWhiteSpace(spec.Name |> Option.defaultValue "")) ->
            { Domain.defaultOntologyProfile with Name = spec.Name.Value }
        | _ -> { Domain.defaultOntologyProfile with Name = "tmforum.tr292-common-core" }

    let private buildStructuredTargets (request: IntentFvo) (payload: JsonObject) : CanonicalTarget list =
        let candidates =
            [ "targets"; "target"; "scope"; "resource"; "service"; "node"; "region" ]
            |> List.collect (fun key ->
                match payload[key] with
                | null -> []
                | node -> collectStringValues node)

        let fallback =
            [ request.Context; Some request.Name ]
            |> List.choose id
            |> List.filter (String.IsNullOrWhiteSpace >> not)

        (if candidates = [] then fallback else candidates)
        |> List.distinct
        |> List.map (targetFromString "resource")

    let private buildStructuredExpectations (request: IntentFvo) (payload: JsonObject) : CanonicalExpectation list =
        let candidates =
            [ "expectations"; "expectation"; "reporting"; "constraints"; "goals"; "objective" ]
            |> List.collect (fun key ->
                match payload[key] with
                | null -> []
                | node -> collectStringValues node)

        let descriptions =
            candidates
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> List.distinct

        match descriptions with
        | [] ->
            [ mkExpectation "PropertyExpectation" "intent-state" request.Description None None ]
        | xs ->
            xs |> List.map (fun item -> mkExpectation "PropertyExpectation" item (Some item) None None)

    let normalizeStructured (request: IntentFvo) (classification: InputKind) : CanonicalIntentIr =
        let payload =
            match request.Expression.ExpressionValue.ValueKind with
            | JsonValueKind.Object -> ensureObject request.Expression.ExpressionValue
            | JsonValueKind.Array ->
                let wrapper = JsonObject()
                wrapper["targets"] <- cloneNode request.Expression.ExpressionValue
                wrapper
            | _ -> JsonObject()

        let targets = buildStructuredTargets request payload
        let expectations = buildStructuredExpectations request payload

        { IntentName = request.Name
          Description = request.Description
          Targets = targets
          Expectations = expectations
          Context = request.Context
          Priority = request.Priority
          Profile = inferProfile request
          SourceClassification = classification
          SourceText = None
          SourceIri = Some request.Expression.Iri
          RawExpressionType = request.Expression.Type }

    let private artifactRoot () =
        Path.Combine(AppContext.BaseDirectory, "tmf921-artifacts", "intent-pipeline")

    let private writeCheckReport path requestId witnessName (result: IntentAdmission.WitnessCheckResult) =
        writeIndented
            path
            {| requestId = requestId
               witness = witnessName
               success = result.Success
               checkerVersion = result.CheckerVersion
               stdout = result.Stdout
               stderr = result.Stderr
               diagnostics = result.Diagnostics |}

    let private writeRequestArtifacts
        (requestId: string)
        (rawText: string option)
        (llmPrompt: string option)
        (llmResponse: string option)
        (operationalIntent: OperationalIntentRecord option)
        (canonicalIntent: CanonicalIntentIr option)
        (ontologyValidationReport: JsonElement option)
        (normalizedJsonLd: JsonElement option)
        (candidateCheck: IntentAdmission.WitnessCheckResult option)
        (tmWitness: IntentAdmission.WitnessCheckResult option)
        (providerWitness: IntentAdmission.WitnessCheckResult option) =
        let directory = Path.Combine(artifactRoot (), requestId)
        Directory.CreateDirectory(directory) |> ignore

        let requestPath = Path.Combine(directory, "request.json")
        let rawIntentPath = Path.Combine(directory, "raw-intent.txt")
        let llmPromptPath = Path.Combine(directory, "llm-prompt.txt")
        let llmResponsePath = Path.Combine(directory, "llm-response.json")
        let operationalIntentPath = Path.Combine(directory, "operational-intent.json")
        let ontologyRawIntentPath = Path.Combine(directory, "ontology-raw-intent.json")
        let ontologyValidationReportPath = Path.Combine(directory, "ontology-validation-report.json")
        let canonicalIrPath = Path.Combine(directory, "canonical-ir.json")
        let normalizedIntentPath = Path.Combine(directory, "normalized-intent.jsonld")
        let candidateModulePath = Path.Combine(directory, "candidate-intent.fst")
        let candidateCheckPath = Path.Combine(directory, "candidate-intent-check.json")
        let tmWitnessModulePath = Path.Combine(directory, "tm-witness.fst")
        let tmWitnessCheckPath = Path.Combine(directory, "tm-witness-check.json")
        let providerWitnessModulePath = Path.Combine(directory, "provider-witness.fst")
        let providerWitnessCheckPath = Path.Combine(directory, "provider-witness-check.json")
        let checkedIntentPath = Path.Combine(directory, "checked-intent.fst")

        writeIndented requestPath {| requestId = requestId; generatedAt = DateTimeOffset.UtcNow |}
        rawText |> Option.iter (fun (text: string) -> File.WriteAllText(rawIntentPath, text, Encoding.UTF8))
        llmPrompt |> Option.iter (fun (text: string) -> File.WriteAllText(llmPromptPath, text, Encoding.UTF8))
        llmResponse |> Option.iter (fun (text: string) -> File.WriteAllText(llmResponsePath, text, Encoding.UTF8))
        operationalIntent |> Option.iter (writeIndented operationalIntentPath)

        canonicalIntent
        |> Option.iter (fun canonical ->
            writeIndented ontologyRawIntentPath canonical
            writeIndented canonicalIrPath canonical)

        ontologyValidationReport
        |> Option.iter (fun (report: JsonElement) -> File.WriteAllText(ontologyValidationReportPath, report.GetRawText(), Encoding.UTF8))

        normalizedJsonLd
        |> Option.iter (fun (normalized: JsonElement) -> File.WriteAllText(normalizedIntentPath, normalized.GetRawText(), Encoding.UTF8))

        candidateCheck
        |> Option.iter (fun (result: IntentAdmission.WitnessCheckResult) ->
            File.WriteAllText(candidateModulePath, result.ModuleText, Encoding.UTF8)
            writeCheckReport candidateCheckPath requestId "candidate_module" result)

        tmWitness
        |> Option.iter (fun (result: IntentAdmission.WitnessCheckResult) ->
            File.WriteAllText(tmWitnessModulePath, result.ModuleText, Encoding.UTF8)
            writeCheckReport tmWitnessCheckPath requestId "tm_witness" result)

        providerWitness
        |> Option.iter (fun (result: IntentAdmission.WitnessCheckResult) ->
            File.WriteAllText(providerWitnessModulePath, result.ModuleText, Encoding.UTF8)
            writeCheckReport providerWitnessCheckPath requestId "provider_witness" result)

        match providerWitness with
        | Some result when result.Success -> File.WriteAllText(checkedIntentPath, result.ModuleText, Encoding.UTF8)
        | _ ->
            match tmWitness with
            | Some result when result.Success -> File.WriteAllText(checkedIntentPath, result.ModuleText, Encoding.UTF8)
            | _ -> ()

        { Request = toArtifactReference requestPath
          RawIntent = toArtifactReference rawIntentPath
          LlmPrompt = toArtifactReference llmPromptPath
          LlmResponse = toArtifactReference llmResponsePath
          OperationalIntent = toArtifactReference operationalIntentPath
          SemanticCore = toArtifactReference operationalIntentPath
          OntologyRawIntent = toArtifactReference ontologyRawIntentPath
          OntologyValidationReport = toArtifactReference ontologyValidationReportPath
          CandidateIntentModule = toArtifactReference candidateModulePath
          CandidateIntentCheck = toArtifactReference candidateCheckPath
          TmWitnessModule = toArtifactReference tmWitnessModulePath
          TmWitnessCheck = toArtifactReference tmWitnessCheckPath
          ProviderWitnessModule = toArtifactReference providerWitnessModulePath
          ProviderWitnessCheck = toArtifactReference providerWitnessCheckPath
          CanonicalIr = toArtifactReference canonicalIrPath
          GeneratedIntent = toArtifactReference candidateModulePath
          CheckResult = toArtifactReference providerWitnessCheckPath |> Option.orElse (toArtifactReference tmWitnessCheckPath)
          NormalizedIntent = toArtifactReference normalizedIntentPath
          CheckedIntent = toArtifactReference checkedIntentPath }

    let private normalizedExpression (request: IntentFvo) (normalizedJsonLd: JsonElement) =
        { request.Expression with
            ExpressionValue = normalizedJsonLd
            Type = Some "JsonLdExpression" }

    let private buildRecord
        now
        intentId
        requestId
        classification
        profile
        operationalIntent
        canonicalIntent
        normalizedJsonLd
        checkedModule
        tmWitnessStatus
        providerWitnessStatus
        selectedProfile
        firstFailedWitness
        admissionOutcome
        llmParse
        artifacts
        diagnostics
        checkerVersion =
        { IntentId = intentId
          RequestId = requestId
          Classification = classification
          Status =
            match admissionOutcome with
            | Some "provider_admitted" -> ProcessingStatus.Checked
            | Some "tm_validated_only"
            | Some "not_admitted" -> ProcessingStatus.Rejected
            | _ -> ProcessingStatus.ClarificationRequired
          Profile = profile
          OperationalIntent = operationalIntent
          CanonicalIntent = canonicalIntent
          NormalizedJsonLd = normalizedJsonLd
          CheckedFStarModule = checkedModule
          TmWitnessStatus = tmWitnessStatus
          ProviderWitnessStatus = providerWitnessStatus
          SelectedProfile = selectedProfile
          FirstFailedWitness = firstFailedWitness
          AdmissionOutcome = admissionOutcome
          LlmParse = llmParse
          Artifacts = artifacts
          Diagnostics = diagnostics
          CheckerVersion = checkerVersion
          CreatedAt = now
          UpdatedAt = now }

    let private distinctDiagnostics (items: ProcessingDiagnostic list) =
        items
        |> List.distinctBy (fun item -> item.Code, item.Message, item.Details |> Option.defaultValue "")

    let private tagAttemptStage stage (attempt: LlmParseAttempt) =
        if String.IsNullOrWhiteSpace stage then
            attempt
        else
            let source =
                if String.IsNullOrWhiteSpace attempt.Source then
                    stage
                else
                    $"{stage}:{attempt.Source}"

            { attempt with Source = source }

    let private mergeLlmParseMetadata stage (previous: LlmParseMetadata option) (current: LlmParseMetadata) =
        let previousProvider = previous |> Option.bind (fun value -> value.Provider)
        let previousModel = previous |> Option.bind (fun value -> value.Model)
        let previousPromptVersion = previous |> Option.bind (fun value -> value.PromptVersion)
        let previousFixtureId = previous |> Option.bind (fun value -> value.FixtureId)

        let previousAttempts =
            previous
            |> Option.map (fun value -> value.Attempts)
            |> Option.defaultValue []

        let currentAttempts = current.Attempts |> List.map (tagAttemptStage stage)

        { Provider = current.Provider |> Option.orElse previousProvider
          Model = current.Model |> Option.orElse previousModel
          PromptVersion = current.PromptVersion |> Option.orElse previousPromptVersion
          SelectedOutcome = current.SelectedOutcome
          UsedFixture =
            current.UsedFixture
            || (previous |> Option.map (fun value -> value.UsedFixture) |> Option.defaultValue false)
          FixtureId = current.FixtureId |> Option.orElse previousFixtureId
          Attempts =
            previousAttempts @ currentAttempts
            |> List.mapi (fun index attempt -> { attempt with Attempt = index + 1 }) }

    let private admissionRepairIssues (checks: IntentAdmission.AdmissionCheckBundle) =
        let details =
            [ Some $"admissionOutcome={checks.AdmissionOutcome}"
              Some $"tmWitnessStatus={checks.TmWitnessStatus}"
              Some $"providerWitnessStatus={checks.ProviderWitnessStatus}"
              checks.SelectedProfile |> Option.map (fun value -> $"selectedProfile={value}")
              checks.FirstFailedWitness |> Option.map (fun value -> $"firstFailedWitness={value}") ]
            |> List.choose id
            |> String.concat "; "

        let summaryMessage =
            match checks.FirstFailedWitness with
            | Some witness -> $"The generated F* module failed the downstream admission witness '{witness}'."
            | None when not checks.CandidateCheck.Success -> "The generated F* candidate module did not type-check."
            | _ -> "The generated F* module failed downstream admission checks."

        distinctDiagnostics
            [ diagnostics
                "DOWNSTREAM_ADMISSION_FAILURE"
                summaryMessage
                (if String.IsNullOrWhiteSpace details then None else Some details)
              yield! checks.CandidateCheck.Diagnostics
              yield! checks.TmWitness |> Option.map (fun value -> value.Diagnostics) |> Option.defaultValue []
              yield! checks.ProviderWitness |> Option.map (fun value -> value.Diagnostics) |> Option.defaultValue [] ]

    let private buildNaturalLanguageFailureOutcome
        now
        intentId
        requestId
        (request: IntentFvo)
        text
        llmParse
        llmPrompt
        llmResponse
        failureDiagnostics =
        let artifacts =
            writeRequestArtifacts
                requestId
                (Some text)
                llmPrompt
                llmResponse
                None
                None
                None
                None
                None
                None
                None

        { NormalizedExpression = request.Expression
          ProcessingRecord =
            buildRecord
                now
                intentId
                requestId
                InputKind.NaturalLanguage
                (inferProfile request)
                None
                None
                None
                None
                None
                None
                None
                None
                None
                llmParse
                (Some artifacts)
                (distinctDiagnostics failureDiagnostics)
                None }

    let rec private processTypedIntentAsync
        repairState
        classification
        now
        requestId
        intentId
        (request: IntentFvo)
        rawText
        llmParse
        llmPrompt
        llmResponse
        candidate
        : Task<PipelineOutcome> =
        task {
            let outputDir = Path.Combine(artifactRoot (), requestId)
            let checks = IntentAdmission.runAdmissionChecks outputDir candidate

            let canRetryCheckerFailure =
                match repairState, rawText, llmParse with
                | Some state, Some _, Some metadata ->
                    not metadata.UsedFixture
                    && state.RetryCount < checkerRepairRetryLimit
                    && checks.AdmissionOutcome <> "provider_admitted"
                | _ -> false

            if canRetryCheckerFailure then
                let state = repairState |> Option.get
                let text = rawText |> Option.get
                let retryContext =
                    { state.GenerationContext with
                        RepairIssues = admissionRepairIssues checks }

                let! generated =
                    state.Generator.GenerateIntentModuleAsync(retryContext, text, CancellationToken.None)

                let mergedMetadata =
                    Some(mergeLlmParseMetadata $"checker_repair_{state.RetryCount + 1}" llmParse generated.Metadata)

                match generated.Envelope with
                | Some envelope when envelope.Status = "parsed" ->
                    match envelope.ModuleText with
                    | None ->
                        return
                            buildNaturalLanguageFailureOutcome
                                now
                                intentId
                                requestId
                                request
                                text
                                mergedMetadata
                                generated.PromptText
                                generated.RawResponseText
                                (generated.Diagnostics @ [ diagnostics "MISSING_MODULE_TEXT" "The parsed result omitted moduleText." None ])
                    | Some moduleText ->
                        match IntentAdmission.tryParseCandidateModule moduleText with
                        | Error parseIssues ->
                            return
                                buildNaturalLanguageFailureOutcome
                                    now
                                    intentId
                                    requestId
                                    request
                                    text
                                    mergedMetadata
                                    generated.PromptText
                                    generated.RawResponseText
                                    (generated.Diagnostics @ parseIssues)
                        | Ok parsed ->
                            let repairedCandidate =
                                { parsed with
                                    Intent =
                                        { parsed.Intent with
                                            SourceText = Some text } }

                            return!
                                processTypedIntentAsync
                                    (Some
                                        { state with
                                            RetryCount = state.RetryCount + 1 })
                                    classification
                                    now
                                    requestId
                                    intentId
                                    request
                                    rawText
                                    mergedMetadata
                                    generated.PromptText
                                    generated.RawResponseText
                                    repairedCandidate
                | Some _ ->
                    return
                        buildNaturalLanguageFailureOutcome
                            now
                            intentId
                            requestId
                            request
                            text
                            mergedMetadata
                            generated.PromptText
                            generated.RawResponseText
                            generated.Diagnostics
                | None ->
                    return
                        buildNaturalLanguageFailureOutcome
                            now
                            intentId
                            requestId
                            request
                            text
                            mergedMetadata
                            generated.PromptText
                            generated.RawResponseText
                            generated.Diagnostics
            else
                let operationalIntent = IntentAdmission.toOperationalIntentRecord candidate.Intent
                let canonical = IntentAdmission.toCanonicalIntent candidate.Intent
                let ontologyValidation =
                    JsonSerializer.SerializeToElement(canonical, serializerOptions)
                    |> RawIntentContracts.validateOntologyRawIntent

                let normalizedJsonLd =
                    if ontologyValidation.Accepted then
                        Some(IntentAdmission.emitJsonLd canonical)
                    else
                        None

                let diagnostics =
                    distinctDiagnostics
                        [ yield! checks.CandidateCheck.Diagnostics
                          yield! checks.TmWitness |> Option.map (fun value -> value.Diagnostics) |> Option.defaultValue []
                          yield! checks.ProviderWitness |> Option.map (fun value -> value.Diagnostics) |> Option.defaultValue []
                          yield! ontologyValidation.Issues ]

                let artifacts =
                    writeRequestArtifacts
                        requestId
                        rawText
                        llmPrompt
                        llmResponse
                        (Some operationalIntent)
                        (Some canonical)
                        (Some ontologyValidation.Report)
                        normalizedJsonLd
                        (Some checks.CandidateCheck)
                        checks.TmWitness
                        checks.ProviderWitness

                let processingRecord =
                    buildRecord
                        now
                        intentId
                        requestId
                        classification
                        canonical.Profile
                        (Some operationalIntent)
                        (Some canonical)
                        normalizedJsonLd
                        (checks.ProviderWitness |> Option.filter (fun value -> value.Success) |> Option.map (fun value -> value.ModuleText))
                        (Some checks.TmWitnessStatus)
                        (Some checks.ProviderWitnessStatus)
                        checks.SelectedProfile
                        checks.FirstFailedWitness
                        (Some checks.AdmissionOutcome)
                        llmParse
                        (Some artifacts)
                        diagnostics
                        (checks.ProviderWitness |> Option.bind (fun value -> value.CheckerVersion) |> Option.orElse checks.CandidateCheck.CheckerVersion)

                let normalizedExpression =
                    match normalizedJsonLd, checks.AdmissionOutcome with
                    | Some normalized, "provider_admitted" -> normalizedExpression request normalized
                    | _ -> request.Expression

                return
                    { NormalizedExpression = normalizedExpression
                      ProcessingRecord = processingRecord }
        }

    let processIntentWithContextAsync
        (rawIntentGenerator: IRawIntentGenerator)
        (generationContext: RawIntentGenerationContext)
        (intentId: string)
        (request: IntentFvo)
        : Task<PipelineOutcome> =
        task {
            let now = DateTimeOffset.UtcNow
            let requestId = Guid.NewGuid().ToString("N")
            let classification = classifyInput request.Expression

            match classification with
            | InputKind.StructuredCanonical
            | InputKind.StructuredNormalizable ->
                let canonical = normalizeStructured request classification

                match IntentAdmission.tryInterpretCanonicalIntent (request.Description |> Option.defaultValue request.Name) canonical with
                | Error conversionIssues ->
                    let artifacts =
                        writeRequestArtifacts
                            requestId
                            None
                            None
                            None
                            None
                            (Some canonical)
                            None
                            None
                            None
                            None
                            None

                    return
                        { NormalizedExpression = request.Expression
                          ProcessingRecord =
                            buildRecord
                                now
                                intentId
                                requestId
                                classification
                                canonical.Profile
                                None
                                (Some canonical)
                                None
                                None
                                None
                                None
                                None
                                None
                                None
                                None
                                (Some artifacts)
                                conversionIssues
                                None }
                | Ok restrictedIntent ->
                    let moduleName = $"StructuredIntent_{IntentAdmission.sanitizeModuleSegment requestId}"
                    let moduleText = IntentAdmission.buildCandidateModule moduleName restrictedIntent

                    match IntentAdmission.tryParseCandidateModule moduleText with
                    | Error parseIssues ->
                        let artifacts =
                            writeRequestArtifacts
                                requestId
                                None
                                None
                                None
                                None
                                (Some canonical)
                                None
                                None
                                None
                                None
                                None

                        return
                            { NormalizedExpression = request.Expression
                              ProcessingRecord =
                                buildRecord
                                    now
                                    intentId
                                    requestId
                                    classification
                                    canonical.Profile
                                    None
                                    (Some canonical)
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    (Some artifacts)
                                    parseIssues
                                    None }
                    | Ok candidate ->
                        return!
                            processTypedIntentAsync
                                None
                                classification
                                now
                                requestId
                                intentId
                                request
                                None
                                None
                                None
                                None
                                candidate
            | InputKind.NaturalLanguage ->
                match extractNaturalLanguageText request.Expression with
                | None ->
                    return
                        { NormalizedExpression = request.Expression
                          ProcessingRecord =
                            buildRecord
                                now
                                intentId
                                requestId
                                InputKind.NaturalLanguage
                                (inferProfile request)
                                None
                                None
                                None
                                None
                                None
                                None
                                None
                                None
                                None
                                None
                                None
                                [ diagnostics "MISSING_TEXT" "The natural-language input was empty." None ]
                                None }
                | Some text ->
                    let! generated =
                        rawIntentGenerator.GenerateIntentModuleAsync(generationContext, text, CancellationToken.None)

                    match generated.Envelope with
                    | Some envelope when envelope.Status = "parsed" ->
                        match envelope.ModuleText with
                        | None ->
                            let artifacts =
                                writeRequestArtifacts
                                    requestId
                                    (Some text)
                                    generated.PromptText
                                    generated.RawResponseText
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None

                            return
                                { NormalizedExpression = request.Expression
                                  ProcessingRecord =
                                    buildRecord
                                        now
                                        intentId
                                        requestId
                                        InputKind.NaturalLanguage
                                        (inferProfile request)
                                        None
                                        None
                                        None
                                        None
                                        None
                                        None
                                        None
                                        None
                                        None
                                        (Some generated.Metadata)
                                        (Some artifacts)
                                        (generated.Diagnostics @ [ diagnostics "MISSING_MODULE_TEXT" "The parsed result omitted moduleText." None ])
                                        None }
                        | Some moduleText ->
                            match IntentAdmission.tryParseCandidateModule moduleText with
                            | Error parseIssues ->
                                let artifacts =
                                    writeRequestArtifacts
                                        requestId
                                        (Some text)
                                        generated.PromptText
                                        generated.RawResponseText
                                        None
                                        None
                                        None
                                        None
                                        None
                                        None
                                        None

                                return
                                    { NormalizedExpression = request.Expression
                                      ProcessingRecord =
                                        buildRecord
                                            now
                                            intentId
                                            requestId
                                            InputKind.NaturalLanguage
                                            (inferProfile request)
                                            None
                                            None
                                            None
                                            None
                                            None
                                            None
                                            None
                                            None
                                            None
                                            (Some generated.Metadata)
                                            (Some artifacts)
                                            (generated.Diagnostics @ parseIssues)
                                            None }
                            | Ok parsed ->
                                let candidate =
                                    { parsed with
                                        Intent =
                                            { parsed.Intent with
                                                SourceText = Some text } }

                                return!
                                    processTypedIntentAsync
                                        (Some
                                            { Generator = rawIntentGenerator
                                              GenerationContext = generationContext
                                              RetryCount = 0 })
                                        InputKind.NaturalLanguage
                                        now
                                        requestId
                                        intentId
                                        request
                                        (Some text)
                                        (Some generated.Metadata)
                                        generated.PromptText
                                        generated.RawResponseText
                                        candidate
                    | Some _ ->
                        let artifacts =
                            writeRequestArtifacts
                                requestId
                                (Some text)
                                generated.PromptText
                                generated.RawResponseText
                                None
                                None
                                None
                                None
                                None
                                None
                                None

                        return
                            { NormalizedExpression = request.Expression
                              ProcessingRecord =
                                buildRecord
                                    now
                                    intentId
                                    requestId
                                    InputKind.NaturalLanguage
                                    (inferProfile request)
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    (Some generated.Metadata)
                                    (Some artifacts)
                                    generated.Diagnostics
                                    None }
                    | None ->
                        let artifacts =
                            writeRequestArtifacts
                                requestId
                                (Some text)
                                generated.PromptText
                                generated.RawResponseText
                                None
                                None
                                None
                                None
                                None
                                None
                                None

                        return
                            { NormalizedExpression = request.Expression
                              ProcessingRecord =
                                buildRecord
                                    now
                                    intentId
                                    requestId
                                    InputKind.NaturalLanguage
                                    (inferProfile request)
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    None
                                    (Some generated.Metadata)
                                    (Some artifacts)
                                    generated.Diagnostics
                                    None }
            | InputKind.Ambiguous
            | _ ->
                return
                    { NormalizedExpression = request.Expression
                      ProcessingRecord =
                        buildRecord
                            now
                            intentId
                            requestId
                            InputKind.Ambiguous
                            (inferProfile request)
                            None
                            None
                            None
                            None
                            None
                            None
                            None
                            None
                            None
                            None
                            None
                            [ diagnostics
                                "AMBIGUOUS_INPUT"
                                "The expression was neither recognizable JSON-LD nor recognizable natural language."
                                None ]
                            None }
        }

    let processIntentAsync
        (rawIntentGenerator: IRawIntentGenerator)
        (intentId: string)
        (request: IntentFvo)
        : Task<PipelineOutcome> =
        processIntentWithContextAsync rawIntentGenerator RawIntentGenerationContext.Live intentId request
