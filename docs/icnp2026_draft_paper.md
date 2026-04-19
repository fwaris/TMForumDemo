# Toward Safe Natural-Language Intent Admission for Autonomous Networks: A TMF921-to-F* Shell for Verified Provider Constraints

**Authors:** Faisal Waris et al.  
**Target venue:** IEEE ICNP 2026  
**Status:** Working draft in Markdown for later conversion to IEEE format

## Abstract

Autonomous and AI-assisted networks increasingly expose intent interfaces to operations teams, service orchestrators, and potentially software agents. This shift creates a safety and reliability problem: natural-language or weakly structured intents are attractive at the management plane, but they are also ambiguous, under-specified, and difficult to validate against provider-specific operational constraints. If such intents are admitted too early into orchestration pipelines, downstream automation can violate policy, over-request resources, or issue unsafe actions.

This paper presents a prototype intent-admission shell for autonomous networks built around the TM Forum TMF921 Intent Management API. The shell accepts structured and natural-language intent expressions, classifies the input, normalizes admissible requests into a canonical intermediate representation and JSON-LD form, and emits machine-checkable artifacts that can be validated using F*. Our design separates two safety gates: a telecom-management gate that checks whether a request is sufficiently specific to qualify as a valid intent, and a provider-admission gate that checks whether the resulting request satisfies venue-, profile-, and policy-specific constraints. We instantiate the approach on a live-broadcast connectivity scenario and show how success and failure cases can be captured as explicit proof obligations, including capacity violations, unrealistic latency requests, and protected-traffic policy violations.

The prototype demonstrates a practical middle ground between free-form LLM-driven intent capture and brittle schema-only validation. Rather than directly trusting generated actions, the shell narrows natural-language requests into a typed intent model, preserves sidecar evidence for auditability, and rejects under-specified or unsafe requests before provider execution. We argue that this architecture is a useful safety pattern for intent-driven autonomous networks and a promising bridge between standards-based intent APIs and formally checked admission control.

## 1. Introduction

Intent-driven networking has long promised a more declarative interface between operator goals and network behavior. In the autonomous-network setting, this promise becomes even more compelling: planners, copilots, and LLM-based agents can generate high-level operational goals faster than humans can translate them into low-level policies. However, as soon as we allow natural-language or agent-generated requests to enter network control workflows, intent management becomes a safety-critical boundary rather than a convenience layer.

Three problems arise immediately.

First, many requests that sound operationally reasonable are not actually valid intents. They may omit measurable targets, time windows, service classes, or reporting conditions. Second, even a syntactically well-formed intent may still be inadmissible for a concrete provider because of capacity bounds, latency floors, booking windows, or protected-traffic constraints. Third, if the admission path is implemented as opaque prompt engineering or ad hoc business logic, operators lose the ability to explain why a request was accepted, normalized, or rejected.

These concerns are particularly relevant to autonomous networks, where intent interfaces may be consumed not only by humans but by software agents acting on behalf of NOCs, orchestration systems, and vertical applications. A useful architecture therefore needs to do more than parse an utterance. It must classify the input source, determine whether the request is specific enough to act on, normalize it into a stable machine representation, and surface proof-relevant constraints before provisioning decisions are made.

This paper describes a prototype shell around the TM Forum TMF921 Intent Management API that addresses this admission problem. The shell is implemented in F# as an API-facing mediation layer and paired with F* models that encode validity predicates over normalized intents. The system accepts TMF921-style `Intent_FVO` payloads; classifies each expression as structured canonical, structured normalizable, natural language, or ambiguous; emits a canonical intent intermediate representation; and records sidecar artifacts including normalized JSON-LD, generated F* modules, checker diagnostics, and hashes for traceability.

Our argument is not that formal methods alone solve autonomous-network safety, nor that natural-language interfaces should be avoided. Instead, we argue for a narrow and practical claim: the boundary between intent ingestion and provider execution is the right place to combine standards-aligned API handling, semantic normalization, and machine-checked admissibility tests.

### Contributions

This draft advances the following contributions:

1. A standards-aligned intent-admission architecture built around TMF921 that accepts both structured and natural-language intent expressions.
2. A two-stage validation model that separates telecom-management validity from provider-specific admissibility.
3. A prototype implementation that emits auditable sidecar artifacts, including canonical IR, normalized JSON-LD, generated F* modules, diagnostics, and checker output.
4. A worked case study for live-broadcast connectivity showing how capacity, latency, reporting, and protected-traffic constraints can be encoded as machine-checkable properties.
5. A discussion of why this pattern is useful for safe autonomous networks, especially when upstream requests may be produced by LLMs or agents.

## 2. Motivation and Problem Statement

A recurring challenge in autonomous-network operations is that operator goals are easy to state informally but hard to admit safely. Consider a request such as:

> Provide a premium 5G broadcast service for the live event at Detroit Stadium on April 25, 2026 from 18:00 to 22:00 America/Detroit. Support up to 200 production devices. Keep uplink latency under 20 ms. Send hourly compliance updates and immediate alerts if service quality degrades. Do not impact emergency-service traffic.

This request is attractive because it is readable, compact, and close to the way a coordinator or operations engineer might describe a service need. But for an admission layer, several questions must be answered before any downstream automation can act on it:

- Does the request identify a concrete target or scope?
- Does it include measurable expectations rather than vague adjectives?
- Is the time window valid?
- Is the request compatible with the provider's capacity and latency envelope?
- Does the policy preserve protected traffic?

A weaker request such as "Make the event network really good and fast for the broadcast" illustrates the opposite case. It expresses a goal, but it is not operationally actionable. Treating such inputs as directly admissible intents encourages unsafe guesswork at the automation boundary.

We therefore define the core problem as follows:

**Intent admission problem.** Given a TMF921 intent submission whose expression may be structured, weakly structured, or natural language, determine whether the submission can be normalized into a sufficiently specific and policy-compliant intent representation for provider-facing execution. If yes, produce a normalized representation and proof-relevant evidence. If no, reject the request with explicit diagnostics.

The design objective is not simply parsing accuracy. It is safe admissibility under incomplete, ambiguous, or policy-violating requests.

## 3. System Overview

Figure material can be added later, but the prototype follows the pipeline below:

1. A client submits a TMF921-style `Intent_FVO` object to the intent API.
2. The shell classifies the expression as one of four input kinds:
   - `StructuredCanonical`
   - `StructuredNormalizable`
   - `NaturalLanguage`
   - `Ambiguous`
3. The shell constructs a canonical intent intermediate representation (IR) with targets, expectations, context, priority, ontology profile, source classification, and source provenance.
4. The shell emits normalized JSON-LD for accepted or normalized inputs.
5. For natural-language cases that can be mapped into the shell's ontology subset, the system generates F* artifacts and checker results.
6. A shell-processing record is stored and exposed through the API for audit and debugging.
7. A provider-admission model checks whether the normalized intent satisfies provider constraints.

The prototype is intentionally conservative. It does not attempt full TMF921 conformance, full ontology coverage, or arbitrary LLM-based execution. Instead, it focuses on the admission boundary and on traceable normalization.

## 4. Design

### 4.1 API-Facing Shell

The shell exposes a subset of TMF921 operations for creating, retrieving, patching, deleting, and inspecting intents. The create path is the critical one: once a client submits an intent payload, the shell invokes a processing pipeline that returns two outputs:

- a normalized expression to be attached to the resulting intent resource, and
- a processing record describing how the expression was interpreted.

This design treats normalization as a first-class effect of intent creation rather than an offline preprocessing step. The API therefore becomes both a storage endpoint and a validation boundary.

### 4.2 Input Classification

The first design choice is explicit source classification. The shell distinguishes:

- already canonical inputs, such as JSON-LD payloads with semantic markers,
- structured but not yet canonical inputs that can be normalized,
- natural-language inputs that require extraction and checking, and
- ambiguous inputs that should not be admitted automatically.

This classification matters because the safety posture should depend on how much semantic work remains to be done. A canonical JSON-LD payload and a free-form sentence should not be trusted equally, even if both are wrapped in the same TMF921 envelope.

### 4.3 Canonical Intent IR

The shell converts accepted inputs into a canonical intent IR with the following elements:

- intent name and description,
- explicit targets,
- explicit expectations,
- contextual metadata such as priority and operational context,
- an ontology profile identifying the supported semantic subset, and
- source provenance, including expression type and source text or IRI.

This IR plays two roles. Operationally, it gives the system a stable internal representation that is simpler than arbitrary JSON payloads. From a safety perspective, it identifies the proof-relevant fields that later checks depend on.

### 4.4 JSON-LD Emission

After normalization, the shell emits JSON-LD aligned with a TM Forum intent ontology subset. This step is useful for interoperability and for later integration with semantic tooling, but it is also valuable internally: a normalized graph representation reduces dependence on the exact phrasing or incidental structure of the original request.

### 4.5 Sidecar Evidence and Auditability

For each processed intent, the shell stores sidecar artifacts that may include:

- canonical IR,
- normalized JSON-LD,
- generated F* module text,
- checker diagnostics,
- checker stdout and stderr,
- artifact paths and hashes.

This is a practical reliability feature. In many AI-assisted systems, the transformation from request to executable object is difficult to audit after the fact. Here, the system preserves enough evidence to explain what was derived, what was checked, and why a request was rejected or accepted.

## 5. Formal Admission Model

Our prototype separates validity into two gates.

### 5.1 TM-Level Validity

The first gate checks whether a normalized intent is sufficiently specified to qualify as an admissible telecom-management intent. In the live-broadcast scenario, the model requires:

- a recognized venue,
- a service class,
- event date components,
- a valid time window,
- a timezone,
- a measurable device-count expectation,
- a measurable uplink-latency expectation,
- a reporting interval,
- and immediate degradation alerts.

This gate rejects under-specified requests before any provider-specific reasoning occurs. Intuitively, it answers: "Is this a real intent, or merely an aspiration?"

### 5.2 Provider-Level Admissibility

The second gate checks whether a TM-valid intent is admissible for a provider profile associated with a venue. In the current model:

- `DetroitStadium` maps to a `LiveBroadcastGold` profile,
- `MetroArena` maps to a `LiveBroadcastSilver` profile.

Each profile defines resource and policy bounds, including:

- maximum supported device count,
- minimum admissible latency bound,
- provider booking window,
- minimum reporting interval,
- preservation of emergency-service traffic,
- and prohibition on preempting reserved public-safety capacity.

These predicates are encoded in F* as functions over a typed `tm_intent` record. Success cases inhabit refined types such as `tm_checked_intent` and `provider_checked_intent`, while invalid cases fail to construct such witnesses.

### 5.3 Why Two Gates Matter

The two-gate design is important because network automation frequently conflates "syntactically parseable" with "safe to act on." Our model distinguishes:

- requests that are too vague to be intents at all,
- requests that are proper intents but not provider-admissible,
- and requests that satisfy both semantic and operational constraints.

This separation improves operator feedback and reduces the temptation to silently coerce vague requests into unintended actions.

## 6. Prototype Implementation

The prototype is implemented as an F# ASP.NET Core service with an intent pipeline and a set of F* models for checked scenarios.

### 6.1 Implementation Elements

The main implementation components are:

- a TMF921 shell API for intent resources,
- a domain model for TMF921-like resources and processing metadata,
- an intent pipeline for classification, normalization, JSON-LD emission, and artifact generation,
- a demo scenario layer that parses selected natural-language requests into a typed domain record,
- and F* modules that encode TM-level and provider-level validity predicates.

### 6.2 Example Scenario Family

The current case study uses live-broadcast service requests for event venues. The repository includes:

- one success case,
- one capacity-failure case,
- one latency-failure case,
- one policy-failure case,
- and one TM-level under-specification case.

These cases exercise distinct failure modes that are common in autonomous-network settings:

- resource over-request,
- unrealistic performance demands,
- unsafe policy requests,
- and under-specified intent submissions.

### 6.3 Role of Natural Language

The prototype does not claim to solve open-domain semantic parsing. Instead, it demonstrates a safer and narrower workflow:

1. admit natural language only when it can be mapped into a known intent schema,
2. preserve the original text as provenance,
3. derive a typed representation,
4. check the result against explicit validity predicates,
5. reject the request if any required field or safety condition is missing.

This is a useful pattern for LLM-backed systems. An LLM may help with extraction or normalization, but it should not be the final authority on admissibility.

## 7. Preliminary Evaluation

This section is intentionally framed as a prototype evaluation. We do not yet claim large-scale benchmarking or production realism.

### 7.1 Research Questions

The current draft addresses the following questions:

- `RQ1:` Can a TMF921-facing shell distinguish under-specified inputs from admissible intents before provider execution?
- `RQ2:` Can provider-specific constraints be represented as machine-checkable predicates over normalized intents?
- `RQ3:` Does preserving sidecar evidence improve traceability of acceptance and rejection decisions?

### 7.2 Method

We evaluate the prototype using the live-broadcast scenario family included in the repository. Each scenario begins as a natural-language service request. The shell parses and normalizes the text into a typed telecom-management intent, after which F* validity predicates are used to determine whether the request can be admitted at TM level and provider level.

The scenario suite currently contains five representative cases:

1. a valid request that should pass both gates,
2. a request whose device count exceeds venue capacity,
3. a request whose latency target is below the provider's admissible bound,
4. a request whose policy would violate protected public-safety rules,
5. and a vague request that should fail TM-level normalization.

### 7.3 Expected Outcomes

The intended outcomes are:

- the success case yields both a TM-valid and provider-valid witness,
- the capacity, latency, and policy cases are TM-valid but provider-invalid,
- the vague request is rejected before provider admission.

These outcomes matter because they show that the model rejects unsafe requests for different reasons at different layers, instead of collapsing all failures into generic parsing errors.

### 7.4 What We Can Claim Today

At the current stage, the prototype supports three credible claims:

1. A standards-aligned intent shell can preserve a clean separation between API acceptance and provider admission.
2. Provider constraints that matter to safety and reliability can be captured as explicit, machine-checkable predicates over normalized intents.
3. Natural-language intent capture can be made more trustworthy when paired with typed normalization and retained evidence artifacts.

### 7.5 What Still Needs to Be Added

For a submission-ready version, this section should be strengthened with:

- execution traces or checker outputs summarized in a table,
- latency measurements for the normalization and checking pipeline,
- additional scenario families beyond live broadcasting,
- and, ideally, a comparison against a schema-only baseline or an unchecked natural-language baseline.

## 8. Discussion

### 8.1 Relevance to Autonomous Networks

Autonomous networks require bounded autonomy. Systems need mechanisms to reject, defer, or escalate requests that are ambiguous or operationally unsafe. The proposed shell is not a full planner, but it provides an important control point: it ensures that downstream automation starts from intents that are explicit enough to reason about.

### 8.2 Relevance to LLM and Agent Safety

If an LLM or software agent generates requests on behalf of an operator, the main safety question is not whether the generated text sounds plausible. The real question is whether the resulting request is:

- semantically specific,
- measurably testable,
- policy compliant,
- and admissible under current provider constraints.

Our architecture treats LLM output as a candidate intent, not as a command. This distinction is central to safe agentic networking.

### 8.3 Standards and Interoperability

Using TMF921 as the external API envelope is valuable because it aligns the work with industry intent-management practices. At the same time, the shell is explicit about its current scope: it implements a subset of the TMF921 resource model and a subset of the supporting ontology. We view this as a feature rather than a weakness for early-stage safety work. Narrowing the semantic surface area makes the admissibility boundary more analyzable.

## 9. Limitations and Future Work

The prototype has several limitations.

First, the natural-language handling is intentionally narrow and scenario-driven. It should not be interpreted as general semantic understanding. Second, the provider model is simplified; real providers would depend on richer topology, telemetry, time-varying state, and multi-domain constraints. Third, the current evaluation is small and illustrative. Fourth, the shell does not yet provide full TMF921 conformance, persistent storage, or end-to-end orchestration.

These limitations suggest several next steps:

- expand the ontology subset and the canonical IR,
- connect admissibility checks to dynamic network state and telemetry,
- support clarification dialogues for ambiguous intents instead of hard rejection,
- and extend the proof model from admission predicates to plan-level safety constraints for autonomous remediation workflows.

One especially promising direction is to connect the TMF921 shell to the broader agentic NOC model already explored in this codebase, where constraints such as approvals, evidence sufficiency, rollback, and read-only restrictions are checked before enforcement actions are allowed. This would allow the paper to evolve from "verified intent admission" toward "verified intent-to-plan safety."

## 10. Related Work

This section needs citation-backed expansion in the next draft. The main related-work clusters are:

- intent-based networking and autonomous-network management,
- TM Forum intent models and telecom semantic ontologies,
- LLMs and agents for network operations,
- formal methods for policy verification and safe automation,
- and trustworthy AI or neuro-symbolic approaches for management-plane reasoning.

For the submission version, we should position this paper specifically against:

- schema-validation-only approaches,
- pure natural-language-to-action pipelines,
- and verification work that starts after planning rather than at intent admission time.

## 11. Conclusion

This paper argued that the admission boundary for network intents is a critical safety and reliability control point in autonomous networks. We presented a prototype TMF921 intent shell that accepts structured and natural-language requests, classifies and normalizes them into a canonical representation, emits auditable semantic artifacts, and checks admissibility using explicit provider constraints encoded in F*. The prototype shows how a practical telecom standards interface can be combined with machine-checked validity predicates to reject vague, over-demanding, or policy-violating requests before execution.

The main lesson is simple: in agentic and AI-assisted network management, the safest place to be conservative is before orchestration begins. Standards-based intent ingestion, typed normalization, and formal admissibility checks provide a promising foundation for that conservatism.

## Appendix A. Candidate Figures and Tables

### Figure 1

End-to-end architecture:

- TMF921 client
- intent shell API
- input classifier
- canonical IR builder
- JSON-LD emitter
- F* artifact generator
- TM-level validity gate
- provider-level validity gate
- shell-processing audit endpoint

### Figure 2

Live-broadcast scenario timeline showing:

- original natural-language request,
- normalized fields,
- TM-level checks,
- provider-level checks,
- accept or reject outcome.

### Table 1

Scenario summary table:

| Scenario | TM-valid | Provider-valid | Primary rejection reason |
|---|---|---|---|
| Broadcast success | Yes | Yes | N/A |
| Capacity failure | Yes | No | Device count exceeds profile capacity |
| Latency failure | Yes | No | Requested latency below provider bound |
| Policy failure | Yes | No | Public-safety policy violation |
| Vague request | No | N/A | Missing measurable target and expectations |

### Table 2

Artifact traceability table:

| Artifact | Purpose |
|---|---|
| Canonical IR | Stable internal representation |
| JSON-LD | Semantic interoperability |
| Generated F* module | Machine-checkable encoding |
| Checker diagnostics | Explain rejections |
| Hashes and paths | Audit and reproducibility |

## Appendix B. Submission TODOs

- Add real citations and bibliography placeholders.
- Add a clear threat model for unsafe or ambiguous agent-generated intents.
- Include code-to-paper mapping notes for reproducibility.
- Add one or two additional scenario families beyond live broadcasting.
- Measure processing overhead for normalization and checking.
- Add a short comparison against a schema-only baseline.
- Convert to IEEE conference LaTeX template after content stabilizes.
