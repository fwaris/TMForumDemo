namespace Tmf921.IntentManagement.Api.Tests

open Xunit
open Tmf921.IntentManagement.Api
open Tmf921.IntentManagement.Api.Tests.TestHelpers

type ScenarioRegressionTests() =
    [<Fact>]
    member _.``Scenario failed witnesses remain stable``() =
        let generator = generatorFromScenarioText ()

        for scenario in DemoScenarios.scenarios do
            let outcome =
                IntentPipeline.processIntentWithContextAsync
                    generator
                    RawIntentGenerationContext.Live
                    ("scenario-" + scenario.Id)
                    (DemoScenarios.buildNaturalLanguageRequest scenario.Text)
                |> fun task -> task.GetAwaiter().GetResult()

            Assert.Equal<string option>(scenario.ExpectedFailedWitness, outcome.ProcessingRecord.FirstFailedWitness)
