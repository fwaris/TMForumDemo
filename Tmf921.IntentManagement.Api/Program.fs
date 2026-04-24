namespace Tmf921.IntentManagement.Api
open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.AI
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open OpenAI.Responses

module Program =
    let exitCode = 0

    let private tryParseInt (value: string) =
        match Int32.TryParse value with
        | true, parsed -> Some parsed
        | _ -> None

    let private tryParseFloat32 (value: string) =
        match Single.TryParse value with
        | true, parsed -> Some parsed
        | _ -> None

    let private tryNonEmpty (value: string) =
        if String.IsNullOrWhiteSpace value then
            None
        else
            Some value

    let private tryGetArgValue (args: string array) (name: string) =
        args
        |> Array.tryFindIndex ((=) name)
        |> Option.bind (fun index ->
            if index + 1 < args.Length then
                Some args[index + 1]
            else
                None)

    let private tryGetLatestDirectory root =
        if Directory.Exists root then
            Directory.GetDirectories(root)
            |> Array.sort
            |> Array.tryLast
        else
            None

    let private resolveIntentLlmOptions (configuration: IConfiguration) =
        let defaults = IntentLlmDefaults.value

        { Model = configuration["IntentLlm:Model"] |> Option.ofObj |> Option.defaultValue defaults.Model
          MaxAttempts =
            configuration["IntentLlm:MaxAttempts"]
            |> Option.ofObj
            |> Option.bind tryParseInt
            |> Option.defaultValue defaults.MaxAttempts
          Temperature =
            configuration["IntentLlm:Temperature"]
            |> Option.ofObj
            |> Option.bind tryParseFloat32
            |> Option.defaultValue defaults.Temperature
          TimeoutSeconds =
            configuration["IntentLlm:TimeoutSeconds"]
            |> Option.ofObj
            |> Option.bind tryParseInt
            |> Option.defaultValue defaults.TimeoutSeconds
          UseScenarioFixtures =
            configuration["IntentLlm:UseScenarioFixtures"]
            |> Option.ofObj
            |> Option.bind (fun value ->
                match Boolean.TryParse value with
                | true, parsed -> Some parsed
                | _ -> None)
            |> Option.defaultValue defaults.UseScenarioFixtures }

    let private resolveOpenAiApiKey (configuration: IConfiguration) =
        configuration["OPENAI_API_KEY"]
        |> Option.ofObj
        |> Option.bind tryNonEmpty
        |> Option.orElseWith (fun () ->
            Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            |> Option.ofObj
            |> Option.bind tryNonEmpty)

    let private createChatClient (model: string) (apiKey: string option) =
        apiKey |> Option.map (fun key -> ResponsesClient(model, key).AsIChatClient())

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)
        let intentLlmOptions = resolveIntentLlmOptions builder.Configuration
        let openAiApiKey = resolveOpenAiApiKey builder.Configuration

        if args |> Array.exists (fun arg -> arg = "--run-demo-scenarios") then
            let options = JsonSerializerOptions(serializerOptions)
            options.WriteIndented <- true
            let rawIntentGenerator =
                RawIntentGenerator(createChatClient intentLlmOptions.Model openAiApiKey, intentLlmOptions)
                :> IRawIntentGenerator

            DemoScenarios.runAllAsync rawIntentGenerator
            |> fun task -> task.GetAwaiter().GetResult()
            |> fun results -> JsonSerializer.Serialize(results, options)
            |> Console.WriteLine
            exitCode
        else if args |> Array.exists (fun arg -> arg = "--run-benchmark-live") then
            let options = JsonSerializerOptions(serializerOptions)
            options.WriteIndented <- true
            let outputDirectory = tryGetArgValue args "--benchmark-output-dir" |> Option.map Path.GetFullPath
            let rawIntentGenerator =
                RawIntentGenerator(createChatClient intentLlmOptions.Model openAiApiKey, intentLlmOptions)
                :> IRawIntentGenerator

            BenchmarkRunner.runLiveAsync rawIntentGenerator outputDirectory
            |> fun task -> task.GetAwaiter().GetResult()
            |> fun results -> JsonSerializer.Serialize(results, options)
            |> Console.WriteLine
            exitCode
        else if args |> Array.exists (fun arg -> arg = "--run-benchmark-replay") then
            let options = JsonSerializerOptions(serializerOptions)
            options.WriteIndented <- true
            let replayPath =
                tryGetArgValue args "--run-benchmark-replay"
                |> Option.orElseWith (fun () ->
                    Path.Combine(repoRoot (), "artifacts", "benchmark-runs")
                    |> tryGetLatestDirectory)
                |> Option.defaultValue (Path.Combine(repoRoot (), "artifacts", "benchmark-runs"))

            BenchmarkRunner.replay replayPath
            |> fun results -> JsonSerializer.Serialize(results, options)
            |> Console.WriteLine
            exitCode
        else if args |> Array.exists (fun arg -> arg = "--run-synthetic-correctness-eval") then
            let options = JsonSerializerOptions(serializerOptions)
            options.WriteIndented <- true
            let outputDirectory = tryGetArgValue args "--benchmark-output-dir" |> Option.map Path.GetFullPath
            let repetitionCount =
                tryGetArgValue args "--benchmark-repetitions"
                |> Option.bind tryParseInt
                |> Option.defaultValue 30
            let expressionCount =
                tryGetArgValue args "--benchmark-expression-count"
                |> Option.bind tryParseInt
                |> Option.defaultValue 10
            let rawIntentGenerator =
                RawIntentGenerator(createChatClient intentLlmOptions.Model openAiApiKey, intentLlmOptions)
                :> IRawIntentGenerator

            BenchmarkRunner.runSyntheticCorrectnessAsync rawIntentGenerator outputDirectory repetitionCount expressionCount
            |> fun task -> task.GetAwaiter().GetResult()
            |> fun results -> JsonSerializer.Serialize(results, options)
            |> Console.WriteLine
            exitCode
        else
            builder.Services
                .AddControllers()
                .AddJsonOptions(fun options ->
                    configureSerializerOptions options.JsonSerializerOptions |> ignore)
            |> ignore
            builder.Services.AddSingleton(intentLlmOptions) |> ignore
            builder.Services
                .AddSingleton<IRawIntentGenerator>(fun _ ->
                    RawIntentGenerator(createChatClient intentLlmOptions.Model openAiApiKey, intentLlmOptions)
                    :> IRawIntentGenerator)
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
