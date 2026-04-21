# Intent Submission Sequence

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant API as IntentController
    participant Pipeline as IntentPipeline
    participant Generator as IRawIntentGenerator
    participant Admission as IntentAdmission / F* checker
    participant Artifacts as Artifact FS
    participant Store as IIntentStore
    participant Shell as ShellStore

    Client->>API: POST /tmf-api/intentManagement/v5/intent\nIntent_FVO payload
    API->>API: Parse JSON body into IntentFvo\nGenerate intentId and resource href
    API->>Pipeline: processIntentAsync(intentId, request)
    Pipeline->>Pipeline: Classify expression\nStructuredCanonical | StructuredNormalizable | NaturalLanguage | Ambiguous

    alt StructuredCanonical or StructuredNormalizable
        Pipeline->>Pipeline: Normalize payload to canonical IR
        Pipeline->>Admission: Interpret canonical intent\nBuild and parse candidate F* module
        Admission-->>Pipeline: Restricted intent candidate
        Pipeline->>Admission: runAdmissionChecks(outputDir, candidate)
        Admission-->>Pipeline: common-core witness, provider witness,\nfirst failed witness, admission outcome
    else NaturalLanguage
        Pipeline->>Generator: GenerateIntentModuleAsync(text)
        Generator-->>Pipeline: Envelope, prompt/response, diagnostics
        alt Envelope status = parsed and moduleText present
            Pipeline->>Admission: Parse candidate module
            Admission-->>Pipeline: Restricted intent candidate
            Pipeline->>Admission: runAdmissionChecks(outputDir, candidate)
            Admission-->>Pipeline: common-core witness, provider witness,\nfirst failed witness, admission outcome
        else Clarification required or invalid envelope
            Pipeline->>Pipeline: Keep original expression\nRecord diagnostics only
        end
    else Ambiguous
        Pipeline->>Pipeline: Record ambiguous-input diagnostics only
    end

    opt Typed candidate available
        Pipeline->>Pipeline: Build operational intent and canonical IR
        Pipeline->>Pipeline: Validate ontology raw intent
        alt Provider admitted
            Pipeline->>Pipeline: Emit normalized JSON-LD\nUse it as stored expression
        else Common-core invalid, provider-invalid, or clarification required
            Pipeline->>Pipeline: Keep original submitted expression
        end
    end

    Pipeline->>Artifacts: Write request, IR, JSON-LD,\nF* modules, and check reports
    Pipeline-->>API: PipelineOutcome\nnormalizedExpression + processingRecord
    API->>Store: Create intent resource
    API->>Shell: Save processing record by intentId
    API-->>Client: 201 Created + intent resource

    Note over Client,Shell: Detailed admission status and artifact references are available later from GET /tmf-api/intentManagement/v5/intent/{id}/shell-processing
```

Notes:

- The sequence reflects the current `IntentController.Create` and `IntentPipeline.processIntentAsync` flow in this repo.
- The create call still persists an intent resource even when shell processing ends in rejection or clarification-required status.
- The resource expression is replaced with normalized JSON-LD only when provider admission succeeds.
