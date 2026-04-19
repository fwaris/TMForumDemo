namespace Tmf921.IntentManagement.Api
open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

module Program =
    let exitCode = 0

    [<EntryPoint>]
    let main args =
        if args |> Array.exists (fun arg -> arg = "--run-demo-scenarios") then
            let options = JsonSerializerOptions(WriteIndented = true)
            options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
            DemoScenarios.runAll()
            |> fun results -> JsonSerializer.Serialize(results, options)
            |> Console.WriteLine
            exitCode
        else
            let builder = WebApplication.CreateBuilder(args)

            builder.Services
                .AddControllers()
                .AddJsonOptions(fun options ->
                    options.JsonSerializerOptions.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull)
            |> ignore
            builder.Services.AddSingleton<IIntentStore, IntentStore>() |> ignore
            builder.Services.AddSingleton<ShellStore>() |> ignore
            builder.Services.AddEndpointsApiExplorer() |> ignore

            let app = builder.Build()

            if app.Environment.IsDevelopment() then
                app.UseDeveloperExceptionPage() |> ignore

            app.Use(Func<HttpContext, RequestDelegate, Task>(fun context next ->
                match ApiRouteCompatibility.tryNormalizePath context.Request.Path with
                | Some rewritten -> context.Request.Path <- rewritten
                | None -> ()
                next.Invoke(context))) |> ignore

            app.Use(Func<HttpContext, RequestDelegate, Task>(fun context next ->
                if context.Request.Path = PathString(AppPaths.Demo) then
                    context.Response.Redirect(AppPaths.DemoRoot, false)
                    Task.CompletedTask
                else
                    next.Invoke(context))) |> ignore

            app.UseRouting() |> ignore
            app.UseDefaultFiles() |> ignore
            app.UseStaticFiles() |> ignore
            app.UseAuthorization() |> ignore
            app.MapControllers() |> ignore

            app.MapGet(AppPaths.Root, Func<string>(fun () -> "TMF921 Intent Management shell API")) |> ignore
            app.MapGet(AppPaths.Health, Func<obj>(fun () -> {| status = "ok"; api = "TMF921"; version = "v5"; mode = "shell" |})) |> ignore

            app.Run()

            exitCode
