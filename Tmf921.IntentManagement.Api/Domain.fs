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
      Name: string option
      [<JsonPropertyName("expression")>]
      Expression: IntentExpression option
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

    let cloneProcessingRecord (record: IntentProcessingRecord) =
        { record with
            CanonicalIntent = record.CanonicalIntent
            NormalizedJsonLd = cloneJsonElementOption record.NormalizedJsonLd }
