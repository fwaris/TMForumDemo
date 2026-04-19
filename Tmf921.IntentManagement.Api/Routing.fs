namespace Tmf921.IntentManagement.Api

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Routing

[<RequireQualifiedAccess>]
module ApiRouteSegments =
    [<Literal>]
    let TmfBase = "tmf-api/intentManagement/v5"

    [<Literal>]
    let Intent = "intent"

    [<Literal>]
    let IntentSpecification = "intentSpecification"

    [<Literal>]
    let IntentReport = "intentReport"

    [<Literal>]
    let Hub = "hub"

    [<Literal>]
    let Listener = "listener"

    [<Literal>]
    let Demo = "demo"

[<RequireQualifiedAccess>]
module ApiRouteTemplates =
    [<Literal>]
    let IntentCollection = "tmf-api/intentManagement/v5/intent"

    [<Literal>]
    let IntentSpecificationCollection = "tmf-api/intentManagement/v5/intentSpecification"

    [<Literal>]
    let IntentReportCollection = "tmf-api/intentManagement/v5/intent/{intentId}/intentReport"

    [<Literal>]
    let HubCollection = "tmf-api/intentManagement/v5/hub"

    [<Literal>]
    let ListenerCollection = "tmf-api/intentManagement/v5/listener"

    [<Literal>]
    let DemoCollection = "tmf-api/intentManagement/v5/demo"

[<RequireQualifiedAccess>]
module ApiRouteNames =
    [<Literal>]
    let IntentGetById = "intent.getById"

    [<Literal>]
    let IntentSpecificationGetById = "intentSpecification.getById"

    [<Literal>]
    let IntentReportGetById = "intentReport.getById"

[<RequireQualifiedAccess>]
module AppPaths =
    [<Literal>]
    let Root = "/"

    [<Literal>]
    let Health = "/health"

    [<Literal>]
    let Demo = "/demo"

    [<Literal>]
    let DemoRoot = "/demo/"

[<RequireQualifiedAccess>]
module ApiRoutePaths =
    let intentItem (id: string) = $"{ApiRouteTemplates.IntentCollection}/{id}"

    let intentSpecificationItem (id: string) = $"{ApiRouteTemplates.IntentSpecificationCollection}/{id}"

    let intentReportItem (intentId: string) (id: string) =
        $"{ApiRouteTemplates.IntentCollection}/{intentId}/{ApiRouteSegments.IntentReport}/{id}"

[<RequireQualifiedAccess>]
module ApiLinks =
    let routeValues (pairs: (string * obj) list) =
        let values = RouteValueDictionary()
        for (name, value) in pairs do
            values[name] <- value
        values

    let private normalizePath path =
        match path with
        | null
        | "" -> AppPaths.Root
        | value when value.StartsWith("/") -> value
        | value -> "/" + value

    let absolutePath (controller: ControllerBase) path =
        let pathBase =
            match controller.Request.PathBase.Value with
            | null -> ""
            | value -> value

        $"{controller.Request.Scheme}://{controller.Request.Host}{pathBase}{normalizePath path}"

    let currentCollection (controller: ControllerBase) =
        absolutePath controller controller.Request.Path.Value

    let fromCurrentCollection (controller: ControllerBase) (id: string) =
        href (currentCollection controller) id

    let linkOrPath (controller: ControllerBase) routeName routeValues fallbackPath =
        match controller.Url.Link(routeName, routeValues) with
        | null -> absolutePath controller fallbackPath
        | link -> link

[<RequireQualifiedAccess>]
module ApiRouteCompatibility =
    let tryNormalizePath (path: PathString) =
        match path.Value with
        | null -> None
        | value when value.Contains("//") ->
            let normalized = value.Replace("//", "/")
            if normalized = AppPaths.Root
               || normalized = AppPaths.Health
               || normalized = AppPaths.Demo
               || normalized.StartsWith(AppPaths.DemoRoot)
               || normalized.StartsWith("/tmf-api/") then
                Some(PathString(normalized))
            else
                None
        | _ -> None
