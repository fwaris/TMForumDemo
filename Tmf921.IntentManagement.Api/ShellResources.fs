namespace Tmf921.IntentManagement.Api

open System
open System.Collections.Concurrent
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization

type ShellStore() =
    let hubs = ConcurrentDictionary<string, JsonObject>()
    let intentSpecifications = ConcurrentDictionary<string, JsonObject>()
    let intentReports = ConcurrentDictionary<string, JsonObject>()
    let processingRecords = ConcurrentDictionary<string, IntentProcessingRecord>()

    member _.Hubs = hubs
    member _.IntentSpecifications = intentSpecifications
    member _.IntentReports = intentReports
    member _.ProcessingRecords = processingRecords

[<AutoOpen>]
module ShellJson =
    let nowText () = DateTimeOffset.UtcNow.ToString("O")

    let cloneNode (element: JsonElement) : JsonNode =
        JsonNode.Parse(element.GetRawText())

    let ensureObject (element: JsonElement) =
        match cloneNode element with
        | :? JsonObject as o -> o
        | _ -> JsonObject()

    let href (basePath: string) (id: string) = $"{basePath}/{id}"

    let serializerOptions =
        let o = JsonSerializerOptions()
        o.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        o

    let private wantedWithRequiredMetadata (obj: JsonObject) (wanted: Set<string>) =
        [ "@type"; "id"; "href" ]
        |> List.fold (fun (acc: Set<string>) name -> if obj.ContainsKey(name) then acc.Add(name) else acc) wanted

    let selectFields (fields: string) (value: 'a) : JsonNode =
        let node = JsonSerializer.SerializeToNode(value, serializerOptions)
        if String.IsNullOrWhiteSpace(fields) then
            node
        else
            let wanted =
                fields.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Set.ofArray

            match node with
            | :? JsonObject as obj ->
                let wanted = wantedWithRequiredMetadata obj wanted
                let result = JsonObject()
                for name in wanted do
                    match obj[name] with
                    | null -> ()
                    | v -> result[name] <- v.DeepClone()
                result :> JsonNode
            | :? JsonArray as arr ->
                let filtered = JsonArray()
                for item in arr do
                    match item with
                    | :? JsonObject as obj ->
                        let wanted = wantedWithRequiredMetadata obj wanted
                        let result = JsonObject()
                        for name in wanted do
                            match obj[name] with
                            | null -> ()
                            | v -> result[name] <- v.DeepClone()
                        filtered.Add(result)
                    | _ -> filtered.Add(item)
                filtered :> JsonNode
            | _ -> node
