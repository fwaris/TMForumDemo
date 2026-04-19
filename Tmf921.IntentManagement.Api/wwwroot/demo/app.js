const expectedLabels = {
  DemoAccept: "Expected accepted",
  DemoRejectProvider: "Expected provider rejection",
  DemoRejectTm: "Expected TM rejection"
};

function firstElement(...selectors) {
  for (const selector of selectors) {
    if (!selector) {
      continue;
    }

    const byId = selector.startsWith("#") ? document.getElementById(selector.slice(1)) : null;
    const element = byId || document.querySelector(selector);
    if (element) {
      return element;
    }
  }

  return null;
}

const scenarioSelect = firstElement("#scenario-select", ".scenario-select");
const scenarioInput = firstElement("#scenario-input", ".scenario-input");
const runButton = firstElement("#run-button", ".run-button");
const runState = firstElement("#run-state", ".run-state");
const summaryStrip = firstElement("#summary-strip", ".summary-strip");
const storyStrip = firstElement("#story-strip", ".story-strip");
const jsonSummary = firstElement("#json-summary");
const tmSummary = firstElement("#tm-summary");
const providerSummary = firstElement("#provider-summary");
const providerProfile = firstElement("#provider-profile");
const providerToken = firstElement("#provider-token");
const jsonIssues = firstElement("#json-issues");
const tmIssues = firstElement("#tm-issues");
const providerIssues = firstElement("#provider-issues");
const normalizedJson = firstElement("#normalized-json");
const generatedProviderFstar = firstElement("#generated-provider-fstar");
const providerCheckerOutput = firstElement("#provider-checker-output");
const auditShellMeta = firstElement("#audit-shell-meta");
const auditShellFstar = firstElement("#audit-shell-fstar");
const referenceFstar = firstElement("#reference-fstar");
const scenarioKicker = firstElement("#scenario-kicker");
const scenarioTitle = firstElement("#scenario-title");
const scenarioBadge = firstElement("#scenario-badge");
const constraintTrace = firstElement("#constraint-trace");
const jsonCard = firstElement("#json-card");
const tmCard = firstElement("#tm-card");
const providerCard = firstElement("#provider-card");

let scenariosById = new Map();

function setText(element, text) {
  if (!element) {
    return;
  }

  element.textContent = text;
}

function formatJson(value) {
  return value ? JSON.stringify(value, null, 2) : "No normalized intent was produced.";
}

function resetList(element) {
  if (element) {
    element.replaceChildren();
  }
}

function renderIssues(listElement, issues, emptyText) {
  if (!listElement) {
    return;
  }

  listElement.replaceChildren();

  if (!issues || issues.length === 0) {
    const item = document.createElement("li");
    item.textContent = emptyText;
    listElement.appendChild(item);
    return;
  }

  issues.forEach((entry) => {
    const item = document.createElement("li");
    item.textContent = `${entry.code}: ${entry.message}`;
    listElement.appendChild(item);
  });
}

function laneClass(card, state) {
  if (!card) {
    return;
  }

  card.className = "lane-card";
  if (state) {
    card.classList.add(state);
  }
}

function outcomeLabel(finalOutcome) {
  switch (finalOutcome) {
    case "accepted":
      return "Accepted end-to-end";
    case "rejected_provider":
      return "Rejected by provider witnesses";
    case "rejected_tm":
      return "Rejected during TM witness construction";
    default:
      return "Unexpected outcome";
  }
}

function generatedTmFStarText(result) {
  if (result.pipeline?.checkedFStarModule) {
    return result.pipeline.checkedFStarModule;
  }

  if (result.pipeline?.diagnostics?.length) {
    return result.pipeline.diagnostics
      .map((diag) => `${diag.code}: ${diag.message}${diag.details ? `\n${diag.details}` : ""}`)
      .join("\n\n");
  }

  return "No shell F* artifact was produced for this request.";
}

function pipelineAuditText(result) {
  if (!result.pipeline) {
    return "No shell-processing audit record is available.";
  }

  return JSON.stringify(
    {
      classification: result.pipeline.classification,
      status: result.pipeline.status,
      checkerVersion: result.pipeline.checkerVersion,
      diagnostics: result.pipeline.diagnostics || [],
      artifacts: result.pipeline.artifacts || null
    },
    null,
    2
  );
}

function providerSummaryText(validation) {
  const provider = validation.dependentProvider;
  if (provider.accepted === true) {
    return `Provider witness succeeds for ${provider.selectedProfile || "the resolved profile"}.`;
  }

  if (provider.accepted === false) {
    return `Provider witness fails at ${provider.failedWitness || "provider_checked_intent"}.`;
  }

  return "Provider witness is skipped because the TM witness does not exist.";
}

function jsonSummaryText(validation) {
  return validation.jsonBaseline.accepted
    ? `${validation.jsonBaseline.dialect} accepts the normalized shape.`
    : `${validation.jsonBaseline.dialect} rejects the normalized shape.`;
}

function tmSummaryText(validation) {
  return validation.dependentTm.accepted
    ? "A TM-level witness can be constructed from the normalized intent."
    : `TM witness construction stops at ${validation.dependentTm.failedWitness || "the TM stage"}.`;
}

function renderConstraintTrace(trace) {
  if (!constraintTrace) {
    return;
  }

  constraintTrace.replaceChildren();

  if (!trace || trace.length === 0) {
    const item = document.createElement("li");
    item.className = "constraint-item";
    item.textContent = "No constraint trace is available.";
    constraintTrace.appendChild(item);
    return;
  }

  trace.forEach((entry) => {
    const item = document.createElement("li");
    item.className = `constraint-item ${entry.status || "skipped"}`;

    const header = document.createElement("div");
    header.className = "constraint-header";

    const stage = document.createElement("span");
    stage.className = "constraint-stage";
    stage.textContent = entry.stage;

    const status = document.createElement("span");
    status.className = `constraint-status ${entry.status || "skipped"}`;
    status.textContent = entry.status || "unknown";

    header.append(stage, status);

    const witness = document.createElement("div");
    witness.className = "constraint-witness";
    witness.textContent = entry.witness;

    const summary = document.createElement("p");
    summary.className = "constraint-summary";
    summary.textContent = entry.summary;

    item.append(header, witness, summary);
    constraintTrace.appendChild(item);
  });
}

function selectedScenario() {
  if (!scenarioSelect) {
    return null;
  }

  return scenariosById.get(scenarioSelect.value) || null;
}

function resetResultPanels() {
  if (summaryStrip) {
    summaryStrip.className = "summary-strip";
  }

  laneClass(jsonCard, null);
  laneClass(tmCard, null);
  laneClass(providerCard, null);

  setText(summaryStrip, "Choose a scenario and run validation.");
  setText(storyStrip, "The demo will explain why the selected case is more than schema validation.");
  setText(jsonSummary, "");
  setText(tmSummary, "");
  setText(providerSummary, "");
  setText(providerProfile, "");
  setText(providerToken, "");
  setText(normalizedJson, "");
  setText(generatedProviderFstar, "");
  setText(providerCheckerOutput, "");
  setText(auditShellMeta, "");
  setText(auditShellFstar, "");
  setText(referenceFstar, "");
  resetList(jsonIssues);
  resetList(tmIssues);
  resetList(providerIssues);
  renderConstraintTrace([]);
}

function updateScenarioDetails() {
  const scenario = selectedScenario();
  if (!scenario) {
    return;
  }

  resetResultPanels();
  setText(scenarioKicker, scenario.kicker || "Scenario");
  setText(scenarioTitle, scenario.title || scenario.id);
  setText(scenarioBadge, expectedLabels[scenario.expectedOutcome] || scenario.expectedOutcome);
  if (scenarioInput) {
    scenarioInput.value = scenario.text;
  }
  setText(storyStrip, scenario.story || "This scenario will show how the witness chain differs from schema validation.");
}

function applyResult(result) {
  const validation = result.validation;
  const scenario = result.scenario;

  if (summaryStrip) {
    summaryStrip.className = "summary-strip";
    summaryStrip.classList.add(validation.finalOutcome === "accepted" ? "success" : "failure");
  }

  setText(summaryStrip, `${outcomeLabel(validation.finalOutcome)}. ${scenario.expectedMessage}`);
  setText(storyStrip, validation.story || scenario.story || "");

  setText(jsonSummary, jsonSummaryText(validation));
  setText(tmSummary, tmSummaryText(validation));
  setText(providerSummary, providerSummaryText(validation));
  setText(providerProfile, validation.dependentProvider.selectedProfile
    ? `Resolved profile: ${validation.dependentProvider.selectedProfile}`
    : "Resolved profile: not available");
  setText(providerToken, validation.dependentProvider.admissionTokenType
    ? `Downstream artifact: ${validation.dependentProvider.admissionTokenType}`
    : "Downstream artifact: not constructible");

  laneClass(jsonCard, validation.jsonBaseline.accepted ? "success" : "failure");
  laneClass(tmCard, validation.dependentTm.accepted ? "success" : "failure");
  laneClass(
    providerCard,
    validation.dependentProvider.accepted === true
      ? "success"
      : validation.dependentProvider.accepted === false
        ? "failure"
        : "skipped"
  );

  renderIssues(jsonIssues, validation.jsonBaseline.issues || [], "No JSON baseline issues.");
  renderIssues(tmIssues, validation.dependentTm.issues || [], "No TM witness issues.");
  renderIssues(providerIssues, validation.dependentProvider.issues || [], "No provider witness issues.");
  renderConstraintTrace(validation.constraintTrace || []);

  setText(normalizedJson, formatJson(validation.normalizedIntent));
  setText(generatedProviderFstar, validation.dependentProvider.generatedModule || "No provider F* module was produced.");
  setText(providerCheckerOutput, validation.dependentProvider.checkerExcerpt || "No checker excerpt is available.");
  setText(auditShellMeta, pipelineAuditText(result));
  setText(auditShellFstar, generatedTmFStarText(result));
  setText(referenceFstar, scenario.referenceFStar || "No reference F* file is attached to this scenario.");
}

async function validateScenario() {
  const scenario = selectedScenario();
  if (!scenario) {
    return;
  }

  if (runButton) {
    runButton.disabled = true;
  }
  setText(runState, "Running backend validation...");

  try {
    const response = await fetch("/tmf-api/intentManagement/v5/demo/validate", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        scenarioId: scenario.id,
        text: scenarioInput?.value || scenario.text
      })
    });

    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}`);
    }

    const result = await response.json();
    applyResult(result);
    setText(runState, "Validation complete");
  } catch (error) {
    if (summaryStrip) {
      summaryStrip.className = "summary-strip failure";
    }
    setText(summaryStrip, `Validation request failed. ${error.message}`);
    setText(runState, "Backend request failed");
  } finally {
    if (runButton) {
      runButton.disabled = false;
    }
  }
}

async function loadHealth() {
  const dot = document.getElementById("api-status-dot");
  const text = document.getElementById("api-status-text");

  try {
    const response = await fetch("/health");
    if (!response.ok) {
      throw new Error(`status ${response.status}`);
    }

    const payload = await response.json();
    dot.classList.add("ok");
    setText(text, `API ready: ${payload.api} ${payload.version}`);
  } catch (error) {
    dot.classList.add("fail");
    setText(text, `API unavailable: ${error.message}`);
  }
}

async function loadScenarios() {
  const response = await fetch("/tmf-api/intentManagement/v5/demo/featured-scenarios");
  if (!response.ok) {
    throw new Error(`Unable to load scenarios (${response.status})`);
  }

  const scenarios = await response.json();
  scenariosById = new Map(scenarios.map((scenario) => [scenario.id, scenario]));

  scenarios.forEach((scenario) => {
    const option = document.createElement("option");
    option.value = scenario.id;
    option.textContent = scenario.title || scenario.id;
    scenarioSelect?.appendChild(option);
  });

  if (scenarios.length > 0 && scenarioSelect) {
    scenarioSelect.value = scenarios[0].id;
    updateScenarioDetails();
  }
}

async function bootstrap() {
  runButton?.addEventListener("click", validateScenario);
  scenarioSelect?.addEventListener("change", updateScenarioDetails);

  await loadHealth();

  try {
    await loadScenarios();
  } catch (error) {
    if (summaryStrip) {
      summaryStrip.className = "summary-strip failure";
    }
    setText(summaryStrip, `Unable to load demo scenarios. ${error.message}`);
  }
}

bootstrap();
