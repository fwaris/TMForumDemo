namespace Tmf921.IntentManagement.Api

open System
open System.Diagnostics
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

    type private SidecarCheckResult =
        { Success: bool
          CheckerVersion: string option
          GeneratedModule: string
          Stdout: string
          Stderr: string
          Diagnostics: ProcessingDiagnostic list }

    let private canonicalContext =
        {| icm = "http://www.models.tmforum.org/tio/v1.0.0/IntentCommonModel#"
           tio = "http://www.models.tmforum.org/tio/v1.0.0#"
           quan = "http://www.models.tmforum.org/tio/v1.0.0/QuantityOntology#"
           funn = "http://www.models.tmforum.org/tio/v1.0.0/FunctionOntology#" |}

    let private writeIndented path value =
        let options = JsonSerializerOptions(serializerOptions)
        options.WriteIndented <- true
        File.WriteAllText(path, JsonSerializer.Serialize(value, options))

    let private serializeElement value =
        JsonSerializer.SerializeToElement(value, serializerOptions)

    let private sha256 path =
        use stream = File.OpenRead(path)
        use hash = SHA256.Create()
        hash.ComputeHash(stream)
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""

    let private toArtifactReference path =
        if File.Exists(path) then
            Some { Path = path; Sha256 = Some (sha256 path) }
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
                if String.IsNullOrWhiteSpace text then Some (node.ToJsonString()) else Some text
            with _ ->
                Some (node.ToJsonString())
        | _ -> Some (node.ToJsonString())

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
        | _ ->
            stringOrJson node |> Option.toList

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
        | JsonValueKind.Object ->
            ensureObject expression.ExpressionValue |> classifyObject
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

    let private inferProfile (request: IntentFvo) : OntologyProfile =
        match request.IntentSpecification with
        | Some spec when not (String.IsNullOrWhiteSpace (spec.Name |> Option.defaultValue "")) ->
            { Domain.defaultOntologyProfile with Name = spec.Name.Value }
        | _ -> Domain.defaultOntologyProfile

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

    let emitJsonLd (canonical: CanonicalIntentIr) =
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
        root["@context"] <- serializeElement canonicalContext |> cloneNode
        root["@graph"] <- JsonArray([| intent :> JsonNode |])
        JsonDocument.Parse(root.ToJsonString()).RootElement.Clone()

    let private assembleRawIntent (request: IntentFvo) (text: string) (envelope: RawIntentParseEnvelope) =
        let toCanonicalTarget (value: RawIntentTarget) : CanonicalTarget =
            { Id = value.Id |> Option.defaultValue ""
              TargetType = value.TargetType
              Name = value.Name }

        let toCanonicalConditionClause (value: RawIntentConditionClause) : CanonicalCondition =
            { Kind = value.Kind |> Option.defaultValue ""
              Subject = value.Subject
              Operator = value.Operator
              Value = value.Value
              Children = [] }

        let toCanonicalCondition (value: RawIntentCondition) : CanonicalCondition =
            { Kind = value.Kind |> Option.defaultValue ""
              Subject = value.Subject
              Operator = value.Operator
              Value = value.Value
              Children = value.Children |> List.map toCanonicalConditionClause }

        let toCanonicalQuantity (value: RawIntentQuantity) : CanonicalQuantity =
            { Value = value.Value |> Option.defaultValue ""
              Unit = value.Unit }

        let toCanonicalFunctionApplication (value: RawIntentFunctionApplication) : CanonicalFunctionApplication =
            { Name = value.Name |> Option.defaultValue ""
              Arguments = value.Arguments }

        let toCanonicalExpectation (value: RawIntentExpectation) : CanonicalExpectation =
            { Kind = value.Kind |> Option.defaultValue ""
              Subject = value.Subject |> Option.defaultValue ""
              Description = value.Description
              Condition = value.Condition |> Option.map toCanonicalCondition
              Quantity = value.Quantity |> Option.map toCanonicalQuantity
              FunctionApplication = value.FunctionApplication |> Option.map toCanonicalFunctionApplication }

        match envelope.SemanticCore with
        | None ->
            Error
                [ diagnostics
                    "SCHEMA_MISMATCH"
                    "The parsed natural-language result did not contain semanticCore content."
                    None ]
        | Some semanticCore ->
            Ok
                { IntentName = semanticCore.IntentName |> Option.defaultValue request.Name
                  Description = semanticCore.Description |> Option.orElse request.Description
                  Targets = semanticCore.Targets |> List.map toCanonicalTarget
                  Expectations = semanticCore.Expectations |> List.map toCanonicalExpectation
                  Context = semanticCore.Context
                  Priority = semanticCore.Priority
                  Profile = inferProfile request
                  SourceClassification = InputKind.NaturalLanguage
                  SourceText = Some text
                  SourceIri = Some request.Expression.Iri
                  RawExpressionType = request.Expression.Type }

    let private validateOntologyRawIntent (canonical: CanonicalIntentIr) =
        JsonSerializer.SerializeToElement(canonical, serializerOptions)
        |> RawIntentContracts.validateOntologyRawIntent

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

    let private renderStringList (values: string list) =
        values
        |> List.map (fun item -> $"\"{fstarString item}\"")
        |> String.concat "; "
        |> sprintf "[%s]"

    let private renderTargetDeclaration (index: int) (value: CanonicalTarget) =
        let name = $"target_{index}"
        let declaration =
            $"""let {name} : target_ref =
  {{ id = "{fstarString value.Id}";
     target_type = {renderOptionString value.TargetType};
     name = {renderOptionString value.Name} }}"""

        declaration, name

    let rec private renderConditionDeclaration (name: string) (value: CanonicalCondition) =
        let childDeclarations, childNames =
            value.Children
            |> List.mapi (fun index child -> renderConditionDeclaration $"{name}_child_{index}" child)
            |> List.unzip

        let childrenBlock =
            childDeclarations |> String.concat "\n\n"

        let childRefs = childNames |> String.concat "; "

        let current =
            $"""let {name} : condition =
  {{ kind = "{fstarString value.Kind}";
     subject = {renderOptionString value.Subject};
     operator = {renderOptionString value.Operator};
     value = {renderOptionString value.Value};
     children = [{childRefs}] }}"""

        let declaration =
            if String.IsNullOrWhiteSpace childrenBlock then current else $"{childrenBlock}\n\n{current}"

        declaration, name

    let private renderQuantityDeclaration (name: string) (value: CanonicalQuantity) =
        let declaration =
            $"""let {name} : quantity =
  {{ value = "{fstarString value.Value}";
     unit = {renderOptionString value.Unit} }}"""

        declaration, name

    let private renderFunctionDeclaration (name: string) (value: CanonicalFunctionApplication) =
        let arguments =
            value.Arguments
            |> List.map (fun item -> $"\"{fstarString item}\"")
            |> String.concat "; "

        let declaration =
            $"""let {name} : function_application =
  {{ name = "{fstarString value.Name}";
     arguments = [{arguments}] }}"""

        declaration, name

    let private renderExpectationDeclaration (index: int) (value: CanonicalExpectation) =
        let conditionDeclarations, conditionRef =
            match value.Condition with
            | Some condition ->
                let declaration, name = renderConditionDeclaration $"condition_{index}" condition
                declaration, $"Some {name}"
            | None -> "", "None"

        let quantityDeclarations, quantityRef =
            match value.Quantity with
            | Some quantity ->
                let declaration, name = renderQuantityDeclaration $"quantity_{index}" quantity
                declaration, $"Some {name}"
            | None -> "", "None"

        let functionDeclarations, functionRef =
            match value.FunctionApplication with
            | Some application ->
                let declaration, name = renderFunctionDeclaration $"function_{index}" application
                declaration, $"Some {name}"
            | None -> "", "None"

        let prefix =
            [ conditionDeclarations; quantityDeclarations; functionDeclarations ]
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> String.concat "\n\n"

        let name = $"expectation_{index}"

        let current =
            $"""let {name} : expectation =
  {{ kind = "{fstarString value.Kind}";
     subject = "{fstarString value.Subject}";
     description = {renderOptionString value.Description};
     condition = {conditionRef};
     quantity = {quantityRef};
     function_application = {functionRef} }}"""

        let declaration =
            if String.IsNullOrWhiteSpace prefix then current else $"{prefix}\n\n{current}"

        declaration, name

    let private ontologyLibraryDir () =
        Path.Combine(repoRoot (), "Tmf921.IntentManagement.Api", "FStar")

    let private artifactRoot () =
        Path.Combine(AppContext.BaseDirectory, "tmf921-artifacts", "intent-pipeline")

    let private buildGeneratedModule (moduleName: string) (canonical: CanonicalIntentIr) =
        let targetDeclarations, targetRefs =
            canonical.Targets
            |> List.mapi renderTargetDeclaration
            |> List.unzip

        let expectationDeclarations, expectationRefs =
            canonical.Expectations
            |> List.mapi renderExpectationDeclaration
            |> List.unzip

        let targetBlock = targetDeclarations |> String.concat "\n\n"
        let expectationBlock = expectationDeclarations |> String.concat "\n\n"
        let targetRefs = targetRefs |> String.concat "; "
        let expectationRefs = expectationRefs |> String.concat "; "

        $"""module {moduleName}

open IntentOntology

let ontology_profile : ontology_profile =
  {{ name = "{fstarString canonical.Profile.Name}";
     version = "{fstarString canonical.Profile.Version}";
     enabled_modules = {renderStringList canonical.Profile.EnabledModules} }}

{targetBlock}

{expectationBlock}

let raw_intent_ir : raw_intent_ir =
  {{ intent_name = "{fstarString canonical.IntentName}";
     description = {renderOptionString canonical.Description};
     targets = [{targetRefs}];
     expectations = [{expectationRefs}];
     context = {renderOptionString canonical.Context};
     priority = {renderOptionString canonical.Priority};
     profile_name = "{fstarString canonical.Profile.Name}";
     profile_version = "{fstarString canonical.Profile.Version}";
     enabled_modules = {renderStringList canonical.Profile.EnabledModules};
     source_classification = "{canonical.SourceClassification}";
     source_text = {renderOptionString canonical.SourceText};
     source_iri = {renderOptionString canonical.SourceIri};
     raw_expression_type = {renderOptionString canonical.RawExpressionType} }}

let normalized_intent : canonical_intent_ir =
  raw_intent_ir

let source_metadata : source_metadata =
  {{ classification = "{canonical.SourceClassification}";
     source_text = {renderOptionString canonical.SourceText};
     source_iri = {renderOptionString canonical.SourceIri} }}

let checked_intent : checked_intent ontology_profile raw_intent_ir =
  mk_checked_intent ontology_profile raw_intent_ir
"""

    let private runProcess (fileName: string) (arguments: string) =
        let startInfo = ProcessStartInfo()
        startInfo.FileName <- fileName
        startInfo.Arguments <- arguments
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false

        use proc = new Process()
        proc.StartInfo <- startInfo
        proc.Start() |> ignore
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        proc.ExitCode, stdout, stderr

    let private checkerVersion () =
        let exitCode, stdout, stderr = runProcess "fstar.exe" "--version"
        if exitCode = 0 then
            Some (stdout.Trim())
        else if not (String.IsNullOrWhiteSpace stderr) then
            Some (stderr.Trim())
        else
            None

    let private runFStarCheck (generatedPath: string) (canonical: CanonicalIntentIr) =
        let moduleName = Path.GetFileNameWithoutExtension(generatedPath : string)
        let generatedModule = buildGeneratedModule moduleName canonical
        File.WriteAllText(generatedPath, generatedModule, Encoding.UTF8)

        let includeDir = ontologyLibraryDir ()
        let arguments = $"--include \"{includeDir}\" \"{generatedPath}\""
        let exitCode, stdout, stderr = runProcess "fstar.exe" arguments

        let diagnostics =
            if exitCode = 0 then
                []
            else
                [ diagnostics "ONTOLOGY_CHECK_FAILED" "The generated F* module did not type-check." (Some stderr) ]

        { Success = (exitCode = 0)
          CheckerVersion = checkerVersion ()
          GeneratedModule = generatedModule
          Stdout = stdout
          Stderr = stderr
          Diagnostics = diagnostics }

    let private writeRequestArtifacts
        (requestId: string)
        (rawText: string option)
        (llmPrompt: string option)
        (llmResponse: string option)
        (semanticCore: RawIntentSemanticCore option)
        (ontologyRawIntent: CanonicalIntentIr option)
        (ontologyValidationReport: JsonElement option)
        (normalizedJsonLd: JsonElement option)
        (sidecar: SidecarCheckResult option) =
        let directory = Path.Combine(artifactRoot (), requestId)
        Directory.CreateDirectory(directory) |> ignore

        let requestPath = Path.Combine(directory, "request.json")
        let rawIntentPath = Path.Combine(directory, "raw-intent.txt")
        let llmPromptPath = Path.Combine(directory, "llm-prompt.txt")
        let llmResponsePath = Path.Combine(directory, "llm-response.json")
        let semanticCorePath = Path.Combine(directory, "semantic-core.json")
        let ontologyRawIntentPath = Path.Combine(directory, "ontology-raw-intent.json")
        let ontologyValidationReportPath = Path.Combine(directory, "ontology-validation-report.json")
        let canonicalIrPath = Path.Combine(directory, "canonical-ir.json")
        let generatedIntentPath = Path.Combine(directory, "generated-intent.fst")
        let checkResultPath = Path.Combine(directory, "check-result.json")
        let normalizedIntentPath = Path.Combine(directory, "normalized-intent.jsonld")
        let checkedIntentPath = Path.Combine(directory, "checked-intent.fst")

        writeIndented requestPath {| requestId = requestId; generatedAt = DateTimeOffset.UtcNow |}
        rawText |> Option.iter (fun text -> File.WriteAllText(rawIntentPath, text, Encoding.UTF8))
        llmPrompt |> Option.iter (fun text -> File.WriteAllText(llmPromptPath, text, Encoding.UTF8))
        llmResponse |> Option.iter (fun text -> File.WriteAllText(llmResponsePath, text, Encoding.UTF8))
        semanticCore |> Option.iter (writeIndented semanticCorePath)

        ontologyRawIntent
        |> Option.iter (fun canonical ->
            writeIndented ontologyRawIntentPath canonical
            writeIndented canonicalIrPath canonical)

        ontologyValidationReport
        |> Option.iter (fun report -> File.WriteAllText(ontologyValidationReportPath, report.GetRawText(), Encoding.UTF8))

        normalizedJsonLd
        |> Option.iter (fun normalized -> File.WriteAllText(normalizedIntentPath, normalized.GetRawText(), Encoding.UTF8))

        sidecar
        |> Option.iter (fun result ->
            if not (String.IsNullOrWhiteSpace result.GeneratedModule) then
                File.WriteAllText(generatedIntentPath, result.GeneratedModule, Encoding.UTF8)

            if result.Success && File.Exists(generatedIntentPath) then
                File.Copy(generatedIntentPath, checkedIntentPath, true)

            writeIndented
                checkResultPath
                {| requestId = requestId
                   success = result.Success
                   checkerVersion = result.CheckerVersion
                   stdout = result.Stdout
                   stderr = result.Stderr
                   diagnostics = result.Diagnostics
                   topLevelSymbols =
                       [ "ontology_profile"
                         "raw_intent_ir"
                         "normalized_intent"
                         "source_metadata"
                         "checked_intent" ] |})

        { Request = toArtifactReference requestPath
          RawIntent = toArtifactReference rawIntentPath
          LlmPrompt = toArtifactReference llmPromptPath
          LlmResponse = toArtifactReference llmResponsePath
          SemanticCore = toArtifactReference semanticCorePath
          OntologyRawIntent = toArtifactReference ontologyRawIntentPath
          OntologyValidationReport = toArtifactReference ontologyValidationReportPath
          CanonicalIr = toArtifactReference canonicalIrPath
          GeneratedIntent = toArtifactReference generatedIntentPath
          CheckResult = toArtifactReference checkResultPath
          NormalizedIntent = toArtifactReference normalizedIntentPath
          CheckedIntent = toArtifactReference checkedIntentPath }

    let private normalizedExpression (request: IntentFvo) (normalizedJsonLd: JsonElement) =
        { request.Expression with
            ExpressionValue = normalizedJsonLd
            Type = Some "JsonLdExpression" }

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
                let normalizedJsonLd = emitJsonLd canonical
                let artifacts =
                    writeRequestArtifacts
                        requestId
                        None
                        None
                        None
                        None
                        (Some canonical)
                        None
                        (Some normalizedJsonLd)
                        None

                return
                    { NormalizedExpression = normalizedExpression request normalizedJsonLd
                      ProcessingRecord =
                        { IntentId = intentId
                          RequestId = requestId
                          Classification = classification
                          Status =
                            if classification = InputKind.StructuredCanonical
                            then ProcessingStatus.Bypassed
                            else ProcessingStatus.Normalized
                          Profile = canonical.Profile
                          CanonicalIntent = Some canonical
                          NormalizedJsonLd = Some normalizedJsonLd
                          CheckedFStarModule = None
                          LlmParse = None
                          Artifacts = Some artifacts
                          Diagnostics = []
                          CheckerVersion = None
                          CreatedAt = now
                          UpdatedAt = now } }
            | InputKind.NaturalLanguage ->
                match extractNaturalLanguageText request.Expression with
                | None ->
                    return
                        { NormalizedExpression = request.Expression
                          ProcessingRecord =
                            { IntentId = intentId
                              RequestId = requestId
                              Classification = InputKind.NaturalLanguage
                              Status = ProcessingStatus.ClarificationRequired
                              Profile = inferProfile request
                              CanonicalIntent = None
                              NormalizedJsonLd = None
                              CheckedFStarModule = None
                              LlmParse = None
                              Artifacts = None
                              Diagnostics = [ diagnostics "MISSING_TEXT" "The natural-language input was empty." None ]
                              CheckerVersion = None
                              CreatedAt = now
                              UpdatedAt = now } }
                | Some text ->
                    let! generated =
                        rawIntentGenerator.GenerateSemanticCoreAsync(generationContext, text, CancellationToken.None)

                    let rawSemanticCore =
                        generated.Envelope |> Option.bind (fun envelope -> envelope.SemanticCore)

                    match generated.Envelope with
                    | Some envelope when envelope.Status = "parsed" ->
                        match assembleRawIntent request text envelope with
                        | Error diags ->
                            let artifacts =
                                writeRequestArtifacts
                                    requestId
                                    (Some text)
                                    generated.PromptText
                                    generated.RawResponseText
                                    rawSemanticCore
                                    None
                                    None
                                    None
                                    None

                            return
                                { NormalizedExpression = request.Expression
                                  ProcessingRecord =
                                    { IntentId = intentId
                                      RequestId = requestId
                                      Classification = InputKind.NaturalLanguage
                                      Status = ProcessingStatus.ClarificationRequired
                                      Profile = inferProfile request
                                      CanonicalIntent = None
                                      NormalizedJsonLd = None
                                      CheckedFStarModule = None
                                      LlmParse = Some generated.Metadata
                                      Artifacts = Some artifacts
                                      Diagnostics = generated.Diagnostics @ diags
                                      CheckerVersion = None
                                      CreatedAt = now
                                      UpdatedAt = now } }
                        | Ok canonical ->
                            let ontologyValidation = validateOntologyRawIntent canonical

                            if not ontologyValidation.Accepted then
                                let artifacts =
                                    writeRequestArtifacts
                                        requestId
                                        (Some text)
                                        generated.PromptText
                                        generated.RawResponseText
                                        rawSemanticCore
                                        (Some canonical)
                                        (Some ontologyValidation.Report)
                                        None
                                        None

                                return
                                    { NormalizedExpression = request.Expression
                                      ProcessingRecord =
                                        { IntentId = intentId
                                          RequestId = requestId
                                          Classification = InputKind.NaturalLanguage
                                          Status = ProcessingStatus.ClarificationRequired
                                          Profile = canonical.Profile
                                          CanonicalIntent = Some canonical
                                          NormalizedJsonLd = None
                                          CheckedFStarModule = None
                                          LlmParse = Some generated.Metadata
                                          Artifacts = Some artifacts
                                          Diagnostics = generated.Diagnostics @ ontologyValidation.Issues
                                          CheckerVersion = None
                                          CreatedAt = now
                                          UpdatedAt = now } }
                            else
                                let normalizedJsonLd = emitJsonLd canonical
                                let moduleName = $"CheckedIntent_{requestId}"
                                let generatedPath = Path.Combine(Path.Combine(artifactRoot (), requestId), $"{moduleName}.fst")
                                Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)) |> ignore
                                let sidecar = runFStarCheck generatedPath canonical

                                let artifacts =
                                    writeRequestArtifacts
                                        requestId
                                        (Some text)
                                        generated.PromptText
                                        generated.RawResponseText
                                        rawSemanticCore
                                        (Some canonical)
                                        (Some ontologyValidation.Report)
                                        (if sidecar.Success then Some normalizedJsonLd else None)
                                        (Some sidecar)

                                return
                                    { NormalizedExpression =
                                        if sidecar.Success then normalizedExpression request normalizedJsonLd else request.Expression
                                      ProcessingRecord =
                                        { IntentId = intentId
                                          RequestId = requestId
                                          Classification = InputKind.NaturalLanguage
                                          Status =
                                            if sidecar.Success
                                            then ProcessingStatus.Checked
                                            else ProcessingStatus.ClarificationRequired
                                          Profile = canonical.Profile
                                          CanonicalIntent = Some canonical
                                          NormalizedJsonLd = if sidecar.Success then Some normalizedJsonLd else None
                                          CheckedFStarModule = if sidecar.Success then Some sidecar.GeneratedModule else None
                                          LlmParse = Some generated.Metadata
                                          Artifacts = Some artifacts
                                          Diagnostics = generated.Diagnostics @ sidecar.Diagnostics
                                          CheckerVersion = sidecar.CheckerVersion
                                          CreatedAt = now
                                          UpdatedAt = now } }
                    | Some envelope ->
                        let artifacts =
                            writeRequestArtifacts
                                requestId
                                (Some text)
                                generated.PromptText
                                generated.RawResponseText
                                rawSemanticCore
                                None
                                None
                                None
                                None

                        return
                            { NormalizedExpression = request.Expression
                              ProcessingRecord =
                                { IntentId = intentId
                                  RequestId = requestId
                                  Classification = InputKind.NaturalLanguage
                                  Status = ProcessingStatus.ClarificationRequired
                                  Profile = inferProfile request
                                  CanonicalIntent = None
                                  NormalizedJsonLd = None
                                  CheckedFStarModule = None
                                  LlmParse = Some generated.Metadata
                                  Artifacts = Some artifacts
                                  Diagnostics = generated.Diagnostics
                                  CheckerVersion = None
                                  CreatedAt = now
                                  UpdatedAt = now } }
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

                        return
                            { NormalizedExpression = request.Expression
                              ProcessingRecord =
                                { IntentId = intentId
                                  RequestId = requestId
                                  Classification = InputKind.NaturalLanguage
                                  Status = ProcessingStatus.ClarificationRequired
                                  Profile = inferProfile request
                                  CanonicalIntent = None
                                  NormalizedJsonLd = None
                                  CheckedFStarModule = None
                                  LlmParse = Some generated.Metadata
                                  Artifacts = Some artifacts
                                  Diagnostics = generated.Diagnostics
                                  CheckerVersion = None
                                  CreatedAt = now
                                  UpdatedAt = now } }
            | InputKind.Ambiguous
            | _ ->
                return
                    { NormalizedExpression = request.Expression
                      ProcessingRecord =
                        { IntentId = intentId
                          RequestId = requestId
                          Classification = InputKind.Ambiguous
                          Status = ProcessingStatus.ClarificationRequired
                          Profile = inferProfile request
                          CanonicalIntent = None
                          NormalizedJsonLd = None
                          CheckedFStarModule = None
                          LlmParse = None
                          Artifacts = None
                          Diagnostics =
                            [ diagnostics
                                "AMBIGUOUS_INPUT"
                                "The expression was neither recognizable JSON-LD nor recognizable natural language."
                                None ]
                          CheckerVersion = None
                          CreatedAt = now
                          UpdatedAt = now } }
        }

    let processIntentAsync
        (rawIntentGenerator: IRawIntentGenerator)
        (intentId: string)
        (request: IntentFvo)
        : Task<PipelineOutcome> =
        processIntentWithContextAsync rawIntentGenerator RawIntentGenerationContext.Live intentId request
