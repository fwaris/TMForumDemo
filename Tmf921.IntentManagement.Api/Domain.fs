namespace Tmf921.IntentManagement.Api

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization

[<CLIMutable>]
type TimePeriod =
    { [<JsonPropertyName("startDateTime")>]
      StartDateTime: DateTimeOffset option
      [<JsonPropertyName("endDateTime")>]
      EndDateTime: DateTimeOffset option }

[<CLIMutable>]
type EntityRef =
    { [<JsonPropertyName("id")>]
      Id: string option
      [<JsonPropertyName("href")>]
      Href: string option
      [<JsonPropertyName("name")>]
      Name: string option
      [<JsonPropertyName("@referredType")>]
      ReferredType: string option
      [<JsonPropertyName("@type")>]
      Type: string option }

[<CLIMutable>]
type IntentExpression =
    { [<JsonPropertyName("iri")>]
      Iri: string
      [<JsonPropertyName("expressionValue")>]
      ExpressionValue: JsonElement
      [<JsonPropertyName("@type")>]
      Type: string option
      [<JsonPropertyName("@baseType")>]
      BaseType: string option
      [<JsonPropertyName("@schemaLocation")>]
      SchemaLocation: string option }

[<CLIMutable>]
type IntentFvo =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("expression")>]
      Expression: IntentExpression
      [<JsonPropertyName("description")>]
      Description: string option
      [<JsonPropertyName("validFor")>]
      ValidFor: TimePeriod option
      [<JsonPropertyName("isBundle")>]
      IsBundle: bool option
      [<JsonPropertyName("priority")>]
      Priority: string option
      [<JsonPropertyName("context")>]
      Context: string option
      [<JsonPropertyName("version")>]
      Version: string option
      [<JsonPropertyName("intentSpecification")>]
      IntentSpecification: EntityRef option
      [<JsonPropertyName("lifecycleStatus")>]
      LifecycleStatus: string option
      [<JsonPropertyName("@type")>]
      Type: string option
      [<JsonPropertyName("@baseType")>]
      BaseType: string option
      [<JsonPropertyName("@schemaLocation")>]
      SchemaLocation: string option }

[<CLIMutable>]
type IntentMvo =
    { [<JsonPropertyName("name")>]
      Name: Skippable<string>
      [<JsonPropertyName("expression")>]
      Expression: Skippable<IntentExpression>
      [<JsonPropertyName("description")>]
      Description: Skippable<string option>
      [<JsonPropertyName("validFor")>]
      ValidFor: Skippable<TimePeriod option>
      [<JsonPropertyName("isBundle")>]
      IsBundle: Skippable<bool option>
      [<JsonPropertyName("priority")>]
      Priority: Skippable<string option>
      [<JsonPropertyName("context")>]
      Context: Skippable<string option>
      [<JsonPropertyName("version")>]
      Version: Skippable<string option>
      [<JsonPropertyName("intentSpecification")>]
      IntentSpecification: Skippable<EntityRef option>
      [<JsonPropertyName("lifecycleStatus")>]
      LifecycleStatus: Skippable<string>
      [<JsonPropertyName("@type")>]
      Type: Skippable<string>
      [<JsonPropertyName("@baseType")>]
      BaseType: Skippable<string option>
      [<JsonPropertyName("@schemaLocation")>]
      SchemaLocation: Skippable<string option> }

[<CLIMutable>]
type IntentResource =
    { [<JsonPropertyName("id")>]
      Id: string
      [<JsonPropertyName("href")>]
      Href: string
      [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("expression")>]
      Expression: IntentExpression
      [<JsonPropertyName("description")>]
      Description: string option
      [<JsonPropertyName("validFor")>]
      ValidFor: TimePeriod option
      [<JsonPropertyName("isBundle")>]
      IsBundle: bool option
      [<JsonPropertyName("priority")>]
      Priority: string option
      [<JsonPropertyName("statusChangeDate")>]
      StatusChangeDate: DateTimeOffset option
      [<JsonPropertyName("context")>]
      Context: string option
      [<JsonPropertyName("version")>]
      Version: string option
      [<JsonPropertyName("intentSpecification")>]
      IntentSpecification: EntityRef option
      [<JsonPropertyName("creationDate")>]
      CreationDate: DateTimeOffset
      [<JsonPropertyName("lastUpdate")>]
      LastUpdate: DateTimeOffset
      [<JsonPropertyName("lifecycleStatus")>]
      LifecycleStatus: string
      [<JsonPropertyName("@type")>]
      Type: string
      [<JsonPropertyName("@baseType")>]
      BaseType: string option
      [<JsonPropertyName("@schemaLocation")>]
      SchemaLocation: string option }

[<CLIMutable>]
type ErrorResponse =
    { [<JsonPropertyName("code")>]
      Code: string
      [<JsonPropertyName("reason")>]
      Reason: string
      [<JsonPropertyName("message")>]
      Message: string
      [<JsonPropertyName("status")>]
      Status: string }

[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type InputKind =
    | StructuredCanonical = 0
    | StructuredNormalizable = 1
    | NaturalLanguage = 2
    | Ambiguous = 3

[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type ProcessingStatus =
    | Bypassed = 0
    | Normalized = 1
    | ClarificationRequired = 2
    | Checked = 3
    | Rejected = 4
    | Failed = 5

[<CLIMutable>]
type OntologyProfile =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("version")>]
      Version: string
      [<JsonPropertyName("enabledModules")>]
      EnabledModules: string list }

[<CLIMutable>]
type CanonicalTarget =
    { [<JsonPropertyName("id")>]
      Id: string
      [<JsonPropertyName("targetType")>]
      TargetType: string option
      [<JsonPropertyName("name")>]
      Name: string option }

[<CLIMutable>]
type CanonicalQuantity =
    { [<JsonPropertyName("value")>]
      Value: string
      [<JsonPropertyName("unit")>]
      Unit: string option }

[<CLIMutable>]
type CanonicalFunctionApplication =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("arguments")>]
      Arguments: string list }

[<CLIMutable>]
type CanonicalCondition =
    { [<JsonPropertyName("kind")>]
      Kind: string
      [<JsonPropertyName("subject")>]
      Subject: string option
      [<JsonPropertyName("operator")>]
      Operator: string option
      [<JsonPropertyName("value")>]
      Value: string option
      [<JsonPropertyName("children")>]
      Children: CanonicalCondition list }

[<CLIMutable>]
type CanonicalExpectation =
    { [<JsonPropertyName("kind")>]
      Kind: string
      [<JsonPropertyName("subject")>]
      Subject: string
      [<JsonPropertyName("description")>]
      Description: string option
      [<JsonPropertyName("condition")>]
      Condition: CanonicalCondition option
      [<JsonPropertyName("quantity")>]
      Quantity: CanonicalQuantity option
      [<JsonPropertyName("functionApplication")>]
      FunctionApplication: CanonicalFunctionApplication option }

[<CLIMutable>]
type CanonicalIntentIr =
    { [<JsonPropertyName("intentName")>]
      IntentName: string
      [<JsonPropertyName("description")>]
      Description: string option
      [<JsonPropertyName("targets")>]
      Targets: CanonicalTarget list
      [<JsonPropertyName("expectations")>]
      Expectations: CanonicalExpectation list
      [<JsonPropertyName("context")>]
      Context: string option
      [<JsonPropertyName("priority")>]
      Priority: string option
      [<JsonPropertyName("profile")>]
      Profile: OntologyProfile
      [<JsonPropertyName("sourceClassification")>]
      SourceClassification: InputKind
      [<JsonPropertyName("sourceText")>]
      SourceText: string option
      [<JsonPropertyName("sourceIri")>]
      SourceIri: string option
      [<JsonPropertyName("rawExpressionType")>]
      RawExpressionType: string option }

[<CLIMutable>]
type ProcessingDiagnostic =
    { [<JsonPropertyName("code")>]
      Code: string
      [<JsonPropertyName("message")>]
      Message: string
      [<JsonPropertyName("details")>]
      Details: string option }

[<CLIMutable>]
type RawIntentTarget =
    { [<JsonPropertyName("id")>]
      Id: string option
      [<JsonPropertyName("targetType")>]
      TargetType: string option
      [<JsonPropertyName("name")>]
      Name: string option }

[<CLIMutable>]
type RawIntentQuantity =
    { [<JsonPropertyName("value")>]
      Value: string option
      [<JsonPropertyName("unit")>]
      Unit: string option }

[<CLIMutable>]
type RawIntentFunctionApplication =
    { [<JsonPropertyName("name")>]
      Name: string option
      [<JsonPropertyName("arguments")>]
      Arguments: string list }

[<CLIMutable>]
type RawIntentConditionClause =
    { [<JsonPropertyName("kind")>]
      Kind: string option
      [<JsonPropertyName("subject")>]
      Subject: string option
      [<JsonPropertyName("operator")>]
      Operator: string option
      [<JsonPropertyName("value")>]
      Value: string option }

[<CLIMutable>]
type RawIntentCondition =
    { [<JsonPropertyName("kind")>]
      Kind: string option
      [<JsonPropertyName("subject")>]
      Subject: string option
      [<JsonPropertyName("operator")>]
      Operator: string option
      [<JsonPropertyName("value")>]
      Value: string option
      [<JsonPropertyName("children")>]
      Children: RawIntentConditionClause list }

[<CLIMutable>]
type RawIntentExpectation =
    { [<JsonPropertyName("kind")>]
      Kind: string option
      [<JsonPropertyName("subject")>]
      Subject: string option
      [<JsonPropertyName("description")>]
      Description: string option
      [<JsonPropertyName("condition")>]
      Condition: RawIntentCondition option
      [<JsonPropertyName("quantity")>]
      Quantity: RawIntentQuantity option
      [<JsonPropertyName("functionApplication")>]
      FunctionApplication: RawIntentFunctionApplication option }

[<CLIMutable>]
type RawIntentSemanticCore =
    { [<JsonPropertyName("intentName")>]
      IntentName: string option
      [<JsonPropertyName("description")>]
      Description: string option
      [<JsonPropertyName("targets")>]
      Targets: RawIntentTarget list
      [<JsonPropertyName("expectations")>]
      Expectations: RawIntentExpectation list
      [<JsonPropertyName("context")>]
      Context: string option
      [<JsonPropertyName("priority")>]
      Priority: string option }

[<CLIMutable>]
type RawIntentParseEnvelope =
    { [<JsonPropertyName("status")>]
      Status: string
      [<JsonPropertyName("semanticCore")>]
      SemanticCore: RawIntentSemanticCore option
      [<JsonPropertyName("issues")>]
      Issues: ProcessingDiagnostic list }

[<CLIMutable>]
type LlmParseAttempt =
    { [<JsonPropertyName("attempt")>]
      Attempt: int
      [<JsonPropertyName("source")>]
      Source: string
      [<JsonPropertyName("outcome")>]
      Outcome: string
      [<JsonPropertyName("responseId")>]
      ResponseId: string option
      [<JsonPropertyName("finishReason")>]
      FinishReason: string option
      [<JsonPropertyName("issues")>]
      Issues: ProcessingDiagnostic list }

[<CLIMutable>]
type LlmParseMetadata =
    { [<JsonPropertyName("provider")>]
      Provider: string option
      [<JsonPropertyName("model")>]
      Model: string option
      [<JsonPropertyName("promptVersion")>]
      PromptVersion: string option
      [<JsonPropertyName("selectedOutcome")>]
      SelectedOutcome: string option
      [<JsonPropertyName("usedFixture")>]
      UsedFixture: bool
      [<JsonPropertyName("fixtureId")>]
      FixtureId: string option
      [<JsonPropertyName("attempts")>]
      Attempts: LlmParseAttempt list }

[<CLIMutable>]
type ArtifactReference =
    { [<JsonPropertyName("path")>]
      Path: string
      [<JsonPropertyName("sha256")>]
      Sha256: string option }

[<CLIMutable>]
type SidecarArtifacts =
    { [<JsonPropertyName("request")>]
      Request: ArtifactReference option
      [<JsonPropertyName("rawIntent")>]
      RawIntent: ArtifactReference option
      [<JsonPropertyName("llmPrompt")>]
      LlmPrompt: ArtifactReference option
      [<JsonPropertyName("llmResponse")>]
      LlmResponse: ArtifactReference option
      [<JsonPropertyName("semanticCore")>]
      SemanticCore: ArtifactReference option
      [<JsonPropertyName("ontologyRawIntent")>]
      OntologyRawIntent: ArtifactReference option
      [<JsonPropertyName("ontologyValidationReport")>]
      OntologyValidationReport: ArtifactReference option
      [<JsonPropertyName("canonicalIr")>]
      CanonicalIr: ArtifactReference option
      [<JsonPropertyName("generatedIntent")>]
      GeneratedIntent: ArtifactReference option
      [<JsonPropertyName("checkResult")>]
      CheckResult: ArtifactReference option
      [<JsonPropertyName("normalizedIntent")>]
      NormalizedIntent: ArtifactReference option
      [<JsonPropertyName("checkedIntent")>]
      CheckedIntent: ArtifactReference option }

[<CLIMutable>]
type IntentProcessingRecord =
    { [<JsonPropertyName("intentId")>]
      IntentId: string
      [<JsonPropertyName("requestId")>]
      RequestId: string
      [<JsonPropertyName("classification")>]
      Classification: InputKind
      [<JsonPropertyName("status")>]
      Status: ProcessingStatus
      [<JsonPropertyName("profile")>]
      Profile: OntologyProfile
      [<JsonPropertyName("canonicalIntent")>]
      CanonicalIntent: CanonicalIntentIr option
      [<JsonPropertyName("normalizedJsonLd")>]
      NormalizedJsonLd: JsonElement option
      [<JsonPropertyName("checkedFStarModule")>]
      CheckedFStarModule: string option
      [<JsonPropertyName("llmParse")>]
      LlmParse: LlmParseMetadata option
      [<JsonPropertyName("artifacts")>]
      Artifacts: SidecarArtifacts option
      [<JsonPropertyName("diagnostics")>]
      Diagnostics: ProcessingDiagnostic list
      [<JsonPropertyName("checkerVersion")>]
      CheckerVersion: string option
      [<JsonPropertyName("createdAt")>]
      CreatedAt: DateTimeOffset
      [<JsonPropertyName("updatedAt")>]
      UpdatedAt: DateTimeOffset }

module Domain =
    let cloneJsonElement (value: JsonElement) =
        value.Clone()

    let cloneJsonElementOption (value: JsonElement option) =
        value |> Option.map cloneJsonElement

    let normalizeExpression (expression: IntentExpression) =
        { expression with
            ExpressionValue = cloneJsonElement expression.ExpressionValue
            Type = expression.Type |> Option.orElse (Some "JsonLdExpression") }

    let defaultOntologyProfile =
        { Name = "tmforum.common-core"
          Version = "3.6.0"
          EnabledModules =
            [ "TR290A"
              "TR290V"
              "TR292A"
              "TR292C"
              "TR292D"
              "TR292E" ] }

    let applySkippableValue (existing: 'T) (patch: Skippable<'T>) =
        match Skippable.toOption patch with
        | Some value when not (isNull (box value)) -> value
        | Some _ -> existing
        | None -> existing

    let applySkippableOption (existing: 'T option) (patch: Skippable<'T option>) =
        match Skippable.toOption patch with
        | Some value -> value
        | None -> existing

    let tryGetSkippableValue (patch: Skippable<'T>) =
        match Skippable.toOption patch with
        | Some value when not (isNull (box value)) -> Some value
        | _ -> None

    let cloneProcessingRecord (record: IntentProcessingRecord) =
        { record with
            CanonicalIntent = record.CanonicalIntent
            NormalizedJsonLd = cloneJsonElementOption record.NormalizedJsonLd }
