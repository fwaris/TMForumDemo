namespace Tmf921.IntentManagement.Api.Controllers

open System
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Tmf921.IntentManagement.Api

[<ApiController>]
[<Route(ApiRouteTemplates.IntentCollection)>]
type IntentController(store: IIntentStore, shellStore: ShellStore, logger: ILogger<IntentController>) =
    inherit ControllerBase()

    let badRequest code reason message =
        { Code = code
          Reason = reason
          Message = message
          Status = "400" }

    let notFound id =
        { Code = "NOT_FOUND"
          Reason = "No matching intent exists."
          Message = $"Intent '{id}' was not found."
          Status = "404" }

    let parseIntOrDefault raw fallback =
        if String.IsNullOrWhiteSpace raw then fallback
        else
            match Int32.TryParse(raw) with
            | true, value -> value
            | _ -> fallback

    let defaultExpression () =
        { Iri = "urn:tmf921:shell:intent"
          ExpressionValue = JsonDocument.Parse("""{"shell":true}""").RootElement.Clone()
          Type = Some "JsonLdExpression"
          BaseType = None
          SchemaLocation = None }

    let nodeString (body: JsonObject) (name: string) =
        match body[name] with
        | null -> None
        | value ->
            match value.GetValueKind() with
            | JsonValueKind.String -> Some (value.GetValue<string>())
            | _ -> Some (value.ToJsonString())

    let nodeBool (body: JsonObject) (name: string) =
        match body[name] with
        | :? JsonValue as value ->
            try Some (value.GetValue<bool>()) with _ -> None
        | _ -> None

    let nodeExpression (body: JsonObject) =
        match body["expression"] with
        | :? JsonObject as expr ->
            let exprValue =
                match expr["expressionValue"] with
                | null -> JsonDocument.Parse("""{"shell":true}""").RootElement.Clone()
                | value -> JsonDocument.Parse(value.ToJsonString()).RootElement.Clone()

            Some
                { Iri = nodeString expr "iri" |> Option.defaultValue "urn:tmf921:shell:intent"
                  ExpressionValue = exprValue
                  Type = nodeString expr "@type" |> Option.orElse (Some "JsonLdExpression")
                  BaseType = nodeString expr "@baseType"
                  SchemaLocation = nodeString expr "@schemaLocation" }
        | _ -> None

    let requestFromJson (payload: JsonElement) : IntentFvo =
        let body =
            match JsonNode.Parse(payload.GetRawText()) with
            | :? JsonObject as o -> o
            | _ -> JsonObject()

        let expression = nodeExpression body |> Option.defaultValue (defaultExpression ())

        { Name = nodeString body "name" |> Option.defaultValue "shellIntent"
          Expression = expression
          Description = nodeString body "description"
          ValidFor = None
          IsBundle = None
          Priority = nodeString body "priority"
          Context = nodeString body "context"
          Version = nodeString body "version"
          IntentSpecification = None
          LifecycleStatus = nodeString body "lifecycleStatus"
          Type = nodeString body "@type"
          BaseType = nodeString body "@baseType"
          SchemaLocation = nodeString body "@schemaLocation" }

    let patchFromJson (payload: JsonElement) : IntentMvo =
        let body =
            match JsonNode.Parse(payload.GetRawText()) with
            | :? JsonObject as o -> o
            | _ -> JsonObject()

        { Name = nodeString body "name"
          Expression = nodeExpression body
          Description = nodeString body "description"
          ValidFor = None
          IsBundle = nodeBool body "isBundle"
          Priority = nodeString body "priority"
          Context = nodeString body "context"
          Version = nodeString body "version"
          IntentSpecification = None
          LifecycleStatus = nodeString body "lifecycleStatus"
          Type = nodeString body "@type"
          BaseType = nodeString body "@baseType"
          SchemaLocation = nodeString body "@schemaLocation" }

    let toResource href id now (request: IntentFvo) =
        { Id = id
          Href = href
          Name = request.Name
          Expression = Domain.normalizeExpression request.Expression
          Description = request.Description
          ValidFor = request.ValidFor
          IsBundle = request.IsBundle
          Priority = request.Priority
          StatusChangeDate = Some now
          Context = request.Context
          Version = request.Version
          IntentSpecification = request.IntentSpecification
          CreationDate = now
          LastUpdate = now
          LifecycleStatus = request.LifecycleStatus |> Option.defaultValue "acknowledged"
          Type = request.Type |> Option.defaultValue "Intent"
          BaseType = request.BaseType
          SchemaLocation = request.SchemaLocation }

    let toRequestFromResource (resource: IntentResource) : IntentFvo =
        { Name = resource.Name
          Expression = resource.Expression
          Description = resource.Description
          ValidFor = resource.ValidFor
          IsBundle = resource.IsBundle
          Priority = resource.Priority
          Context = resource.Context
          Version = resource.Version
          IntentSpecification = resource.IntentSpecification
          LifecycleStatus = Some resource.LifecycleStatus
          Type = Some resource.Type
          BaseType = resource.BaseType
          SchemaLocation = resource.SchemaLocation }

    [<HttpGet>]
    member _.List([<FromQuery>] offset: string, [<FromQuery>] limit: string, [<FromQuery>] fields: string) : IActionResult =
        let offsetValue = parseIntOrDefault offset 0
        let limitValue = parseIntOrDefault limit 50
        logger.LogInformation("Listing intents with offset {Offset} and limit {Limit}", offsetValue, limitValue)

        let result =
            store.List()
            |> List.toSeq
            |> Seq.skip offsetValue
            |> Seq.truncate limitValue
            |> Seq.toList

        base.Ok(selectFields fields result) :> IActionResult

    [<HttpGet("{id}", Name = ApiRouteNames.IntentGetById)>]
    member this.Get(id: string, [<FromQuery>] fields: string) : IActionResult =
        match store.TryGet id with
        | Some intent -> base.Ok(intent) :> IActionResult
        | None -> base.NotFound(notFound id) :> IActionResult

    [<HttpGet("{id}/shell-processing")>]
    member _.GetShellProcessing(id: string) : IActionResult =
        match shellStore.ProcessingRecords.TryGetValue id with
        | true, record -> base.Ok(Domain.cloneProcessingRecord record) :> IActionResult
        | false, _ -> base.NotFound(notFound id) :> IActionResult

    [<HttpPost>]
    member this.Create([<FromBody>] payload: JsonElement, [<FromQuery>] fields: string) : IActionResult =
        let request = requestFromJson payload
        let id = Guid.NewGuid().ToString("N")
        let routeValues = ApiLinks.routeValues [ "id", box id ]
        let resourceHref =
            ApiLinks.linkOrPath this ApiRouteNames.IntentGetById routeValues (ApiRoutePaths.intentItem id)
        let processing = IntentPipeline.processIntent id request
        let now = DateTimeOffset.UtcNow
        let resource =
            toResource resourceHref id now { request with Expression = processing.NormalizedExpression }
        let created = store.Create resource
        shellStore.ProcessingRecords[id] <- processing.ProcessingRecord
        logger.LogInformation("Created TMF921 intent {IntentId} with name {IntentName}", created.Id, created.Name)
        this.CreatedAtRoute(ApiRouteNames.IntentGetById, routeValues, selectFields fields created) :> IActionResult

    [<HttpPatch("{id}")>]
    member this.Patch(id: string, [<FromBody>] payload: JsonElement, [<FromQuery>] fields: string) : IActionResult =
        let patch = patchFromJson payload
        match store.TryGet id with
        | None -> base.NotFound(notFound id) :> IActionResult
        | Some existing ->
            let mergedRequest : IntentFvo =
                { (toRequestFromResource existing) with
                    Name = patch.Name |> Option.defaultValue existing.Name
                    Expression = patch.Expression |> Option.defaultValue existing.Expression
                    Description = patch.Description |> Option.orElse existing.Description
                    ValidFor = patch.ValidFor |> Option.orElse existing.ValidFor
                    IsBundle =
                        match patch.IsBundle with
                        | Some value -> Some value
                        | None -> existing.IsBundle
                    Priority = patch.Priority |> Option.orElse existing.Priority
                    Context = patch.Context |> Option.orElse existing.Context
                    Version = patch.Version |> Option.orElse existing.Version
                    IntentSpecification = patch.IntentSpecification |> Option.orElse existing.IntentSpecification
                    LifecycleStatus = patch.LifecycleStatus |> Option.orElse (Some existing.LifecycleStatus)
                    Type = patch.Type |> Option.orElse (Some existing.Type)
                    BaseType = patch.BaseType |> Option.orElse existing.BaseType
                    SchemaLocation = patch.SchemaLocation |> Option.orElse existing.SchemaLocation }

            let processing =
                match patch.Expression with
                | Some _ -> Some (IntentPipeline.processIntent id mergedRequest)
                | None -> None

            let effectivePatch =
                { patch with
                    Expression = processing |> Option.map (fun outcome -> outcome.NormalizedExpression) }

            match store.Patch(id, effectivePatch) with
            | Some updated ->
                processing |> Option.iter (fun outcome -> shellStore.ProcessingRecords[id] <- outcome.ProcessingRecord)
                logger.LogInformation("Patched TMF921 intent {IntentId}", id)
                base.Ok(selectFields fields updated) :> IActionResult
            | None -> base.NotFound(notFound id) :> IActionResult

    [<HttpDelete("{id}")>]
    member _.Delete(id: string) : IActionResult =
        store.Delete id |> ignore
        shellStore.ProcessingRecords.TryRemove id |> ignore
        logger.LogInformation("Deleted TMF921 intent {IntentId}", id)
        base.NoContent() :> IActionResult
