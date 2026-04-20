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
            |> Domain.tryGetSkippableValue
            |> Option.map Domain.normalizeExpression
            |> Option.defaultValue existing.Expression

        let nextIsBundle = Domain.applySkippableOption existing.IsBundle patch.IsBundle

        let nextLifecycleStatus =
            Domain.applySkippableValue existing.LifecycleStatus patch.LifecycleStatus

        let nextStatusChangeDate =
            if nextLifecycleStatus <> existing.LifecycleStatus then Some DateTimeOffset.UtcNow
            else existing.StatusChangeDate

        { existing with
            Name = Domain.applySkippableValue existing.Name patch.Name
            Expression = nextExpression
            Description = Domain.applySkippableOption existing.Description patch.Description
            ValidFor = Domain.applySkippableOption existing.ValidFor patch.ValidFor
            IsBundle = nextIsBundle
            Priority = Domain.applySkippableOption existing.Priority patch.Priority
            StatusChangeDate = nextStatusChangeDate
            Context = Domain.applySkippableOption existing.Context patch.Context
            Version = Domain.applySkippableOption existing.Version patch.Version
            IntentSpecification = Domain.applySkippableOption existing.IntentSpecification patch.IntentSpecification
            LifecycleStatus = nextLifecycleStatus
            Type = Domain.applySkippableValue existing.Type patch.Type
            BaseType = Domain.applySkippableOption existing.BaseType patch.BaseType
            SchemaLocation = Domain.applySkippableOption existing.SchemaLocation patch.SchemaLocation
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
