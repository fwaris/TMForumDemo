namespace Tmf921.IntentManagement.Api

open System
open System.Collections.Concurrent

type IIntentStore =
    abstract member List : unit -> IntentResource list
    abstract member TryGet : string -> IntentResource option
    abstract member Create : IntentResource -> IntentResource
    abstract member Patch : string * IntentMvo -> IntentResource option
    abstract member Delete : string -> bool

type IntentStore() =
    let items = ConcurrentDictionary<string, IntentResource>()

    let merge (existing: IntentResource) (patch: IntentMvo) =
        let nextExpression =
            patch.Expression
            |> Option.map Domain.normalizeExpression
            |> Option.defaultValue existing.Expression

        let nextIsBundle =
            match patch.IsBundle with
            | Some value -> Some value
            | None -> existing.IsBundle

        let nextLifecycleStatus =
            patch.LifecycleStatus |> Option.defaultValue existing.LifecycleStatus

        let nextStatusChangeDate =
            if nextLifecycleStatus <> existing.LifecycleStatus then Some DateTimeOffset.UtcNow
            else existing.StatusChangeDate

        { existing with
            Name = patch.Name |> Option.defaultValue existing.Name
            Expression = nextExpression
            Description = patch.Description |> Option.orElse existing.Description
            ValidFor = patch.ValidFor |> Option.orElse existing.ValidFor
            IsBundle = nextIsBundle
            Priority = patch.Priority |> Option.orElse existing.Priority
            StatusChangeDate = nextStatusChangeDate
            Context = patch.Context |> Option.orElse existing.Context
            Version = patch.Version |> Option.orElse existing.Version
            IntentSpecification = patch.IntentSpecification |> Option.orElse existing.IntentSpecification
            LifecycleStatus = nextLifecycleStatus
            Type = patch.Type |> Option.defaultValue existing.Type
            BaseType = patch.BaseType |> Option.orElse existing.BaseType
            SchemaLocation = patch.SchemaLocation |> Option.orElse existing.SchemaLocation
            LastUpdate = DateTimeOffset.UtcNow }

    interface IIntentStore with
        member _.List() =
            items.Values |> Seq.sortBy (fun x -> x.CreationDate) |> Seq.toList

        member _.TryGet(id) =
            match items.TryGetValue id with
            | true, value -> Some value
            | false, _ -> None

        member _.Create(intent) =
            items[intent.Id] <- intent
            intent

        member _.Patch(id, patch) =
            match items.TryGetValue id with
            | true, existing ->
                let updated = merge existing patch
                items[id] <- updated
                Some updated
            | false, _ -> None

        member _.Delete(id) =
            items.TryRemove id |> fst
