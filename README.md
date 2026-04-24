# TMF921 Intent Management Shell API

This project is an F# ASP.NET Core shell implementation of the TM Forum **TMF921 Intent Management API**.

**Demo URL after startup:** `http://localhost:8080/demo/`

## Try The Demo First

The fastest way to test the application is the browser demo. Use Docker Compose
when you want the demo to call OpenAI for live natural-language normalization.

Create a local `.env` file:

```bash
cp .env.example .env
```

Edit `.env` and set `OPENAI_API_KEY` to your real key. Then start the app:

```bash
docker compose up --build
```

You can also run with a shell environment variable instead of `.env`:

```bash
docker build -t tmf921-intent-api .
docker run --rm -p 8080:8080 -e OPENAI_API_KEY="$OPENAI_API_KEY" tmf921-intent-api
```

Open the demo UI:

```text
http://localhost:8080/demo/
```

If you only want the checked-in scenario path, the app can still start without a
key, but edited or ad-hoc natural-language demo inputs need `OPENAI_API_KEY`.

Useful demo smoke checks:

```bash
curl http://localhost:8080/health
curl http://localhost:8080/tmf-api/intentManagement/v5/demo/featured-scenarios
curl http://localhost:8080/tmf-api/intentManagement/v5/demo/scenarios/broadcast_success_01
```

To inspect every demo scenario result from the API:

```bash
curl http://localhost:8080/tmf-api/intentManagement/v5/demo/scenarios/run
```

The demo validation API is:

```text
POST /tmf-api/intentManagement/v5/demo/validate
```

The browser page uses that endpoint after loading scenarios from:

```text
GET /tmf-api/intentManagement/v5/demo/featured-scenarios
```

If you run the app locally with `dotnet run`, use `http://localhost:5001/demo/`
instead.

## API Surface

Implemented for v1:

- `GET /health`
- `GET /tmf-api/intentManagement/v5/intent`
- `POST /tmf-api/intentManagement/v5/intent`
- `GET /tmf-api/intentManagement/v5/intent/{id}`
- `GET /tmf-api/intentManagement/v5/intent/{id}/shell-processing`
- `PATCH /tmf-api/intentManagement/v5/intent/{id}`
- `DELETE /tmf-api/intentManagement/v5/intent/{id}`

What this shell does:

- accepts TMF921-style `Intent_FVO` payloads
- classifies each expression as structured JSON-LD, structured normalizable input, natural language, or ambiguous input
- normalizes structured and successfully checked natural-language intents into JSON-LD
- writes file-based sidecar artifacts for canonical IR, normalized JSON-LD, generated F* modules, and checker results
- exposes shell processing metadata and raw checked F* output at `GET /tmf-api/intentManagement/v5/intent/{id}/shell-processing`
- stores intents in memory
- returns a spec-shaped shell resource with `id`, `href`, timestamps, lifecycle status, and normalized expressions where available

What this shell does not do yet:

- full TM921 conformance
- intent specification/report resources
- hub/listener notifications
- persistence
- production-grade LLM orchestration beyond demo NL-to-IR normalization
- advanced ontology coverage beyond the common-core shell subset

## Run Locally

```bash
dotnet run --project Tmf921.IntentManagement.Api/Tmf921.IntentManagement.Api.fsproj
```

Then open:

```text
http://localhost:5001/demo/
```

## Docker

Build and run the container:

```bash
docker build -t tmf921-intent-api .
docker run --rm -p 8080:8080 -e OPENAI_API_KEY="$OPENAI_API_KEY" tmf921-intent-api
```

The image uses Microsoft's ASP.NET runtime base image and installs F* `2025.12.15`.
It defaults to `linux/amd64` because that is the Linux binary published by F*.
The Docker demo uses checked-in scenario fixtures by default, so `/demo/` can
start without an API key, but live demo normalization requires one. The
Dockerfile does not bake in API keys. Pass runtime secrets with environment
variables or Compose when live LLM calls are needed:

```bash
docker run --rm -p 8080:8080 -e OPENAI_API_KEY="$OPENAI_API_KEY" tmf921-intent-api
```

Or use Compose, which reads `OPENAI_API_KEY` from `.env` or your shell:

```bash
cp .env.example .env
# edit .env and set OPENAI_API_KEY
docker compose up --build
```

If your network uses TLS inspection, pass your local CA bundle as a build secret:

```bash
docker build --secret id=ca_bundle,src="$SSL_CERT_FILE" -t tmf921-intent-api .
```

Verify the container:

```bash
curl http://localhost:8080/health
curl http://localhost:8080/tmf-api/intentManagement/v5/demo/featured-scenarios
docker run --rm --entrypoint fstar.exe tmf921-intent-api --version
```

## Sample create request

```bash
curl -X POST http://localhost:5001/tmf-api/intentManagement/v5/intent \
  -H "Content-Type: application/json" \
  -d '{
    "name": "probeIntentExample",
    "description": "A probe intent resource",
    "lifecycleStatus": "Active",
    "version": "1.0",
    "priority": "1",
    "context": "Broadband services",
    "@type": "Intent",
    "expression": {
      "@type": "JsonLdExpression",
      "iri": "https://mycsp.com:8080/tmf-api/rdfs/expression-example-1",
      "expressionValue": {
        "@context": {
          "icm": "http://www.models.tmforum.org/tio/v1.0.0/IntentCommonModel#"
        },
        "idan:EventLiveBroadcast000001": {
          "@type": "icm:Intent",
          "icm:intentOwner": "idan:Salesforce"
        }
      }
    }
  }'
```

## Sample shell-processing response

```bash
curl http://localhost:5001/tmf-api/intentManagement/v5/intent/<intentId>/shell-processing
```

The shell-processing record includes:

- the classified input kind
- the canonical typed IR used by the shell
- normalized JSON-LD
- checker status and diagnostics
- file paths and hashes for generated sidecar artifacts
- the raw checked F* module when natural-language processing succeeded

## Sequence Diagram

See [Intent submission sequence](docs/intent_submission_sequence.md) for the current create-path flow.
