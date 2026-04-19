const featuredScenarioIds = [
  "broadcast_success_01",
  "broadcast_fail_latency_01",
  "broadcast_fail_tm_01"
];

const scenarioTitles = {
  broadcast_success_01: "Accepted Broadcast",
  broadcast_fail_latency_01: "Provider Rejection",
  broadcast_fail_tm_01: "TM Rejection"
};

const scenarioKickers = {
  broadcast_success_01: "Scenario 1",
  broadcast_fail_latency_01: "Scenario 2",
  broadcast_fail_tm_01: "Scenario 3"
};

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
const tmSummary = firstElement("#tm-summary", ".tm-summary");
const providerSummary = firstElement("#provider-summary", ".provider-summary");
const tmIssues = firstElement("#tm-issues", ".tm-issues", ".issue-list.tm-issues");
const providerIssues = firstElement("#provider-issues", ".provider-issues", ".issue-list.provider-issues");
const generatedTmFstar = firstElement("#generated-tm-fstar", "#generated-fstar", ".generated-fstar");
const generatedProviderFstar = firstElement("#generated-provider-fstar");
const normalizedJson = firstElement("#normalized-json", ".normalized-json");
const referenceFstar = firstElement("#reference-fstar", ".reference-fstar");
const scenarioKicker = firstElement("#scenario-kicker", ".scenario-kicker");
const scenarioTitle = firstElement("#scenario-title", ".scenario-title");
const scenarioBadge = firstElement("#scenario-badge", ".scenario-badge");

let scenariosById = new Map();

function formatJson(value) {
  return value ? JSON.stringify(value, null, 2) : "No normalized structure was produced.";
}

function setText(element, text) {
  if (!element) {
    return;
  }

  element.textContent = text;
}

function renderIssues(listElement, issues) {
  if (!listElement) {
    return;
  }

  listElement.replaceChildren();

  if (!issues || issues.length === 0) {
    const item = document.createElement("li");
    item.textContent = "No issues.";
    listElement.appendChild(item);
    return;
  }

  issues.forEach((issue) => {
    const item = document.createElement("li");
    item.textContent = `${issue.code}: ${issue.message}`;
    listElement.appendChild(item);
  });
}

function outcomeLabel(finalOutcome) {
  switch (finalOutcome) {
    case "accepted":
      return "Accepted end-to-end";
    case "rejected_provider":
      return "Rejected by provider constraints";
    case "rejected_tm":
      return "Rejected during TM normalization";
    default:
      return "Unexpected outcome";
  }
}

function generatedTmFStarText(result) {
  if (result.pipeline?.checkedFStarModule) {
    return result.pipeline.checkedFStarModule;
  }

  if (result.pipeline?.diagnostics?.length) {
    const detail = result.pipeline.diagnostics
      .map((diag) => `${diag.code}: ${diag.message}${diag.details ? `\n${diag.details}` : ""}`)
      .join("\n\n");
    return `No checked F* module was produced.\n\n${detail}`;
  }

  return "No checked F* module was produced for this input.";
}

function generatedProviderFStarText(result) {
  if (result.validation.providerFStarModule) {
    return result.validation.providerFStarModule;
  }

  const providerAccepted = result.validation.providerAccepted;
  if (providerAccepted === null || providerAccepted === undefined) {
    return "Provider F* was not generated because provider validation did not run.";
  }

  if (!result.validation.providerDecision?.selectedProfile) {
    return "Provider F* was not generated because the normalized intent could not be mapped to a supported provider profile.";
  }

  return "No provider F* module was produced for this input.";
}

function selectedScenario() {
  if (!scenarioSelect) {
    return null;
  }

  return scenariosById.get(scenarioSelect.value);
}

function updateScenarioDetails() {
  const scenario = selectedScenario();
  if (!scenario) {
    return;
  }

  setText(scenarioKicker, scenarioKickers[scenario.id] || "Scenario");
  setText(scenarioTitle, scenarioTitles[scenario.id] || scenario.id);
  setText(scenarioBadge, expectedLabels[scenario.expectedOutcome] || scenario.expectedOutcome);
  if (scenarioInput) {
    scenarioInput.value = scenario.text;
  }
  setText(summaryStrip, "Press Validate to send the statement to the backend.");
  if (summaryStrip) {
    summaryStrip.className = "summary-strip";
  }
  setText(tmSummary, "");
  setText(providerSummary, "");
  setText(generatedTmFstar, "");
  setText(generatedProviderFstar, "");
  setText(normalizedJson, "");
  setText(referenceFstar, "");
  tmIssues.replaceChildren();
  providerIssues.replaceChildren();
}

function applyResult(result) {
  if (summaryStrip) {
    summaryStrip.className = "summary-strip";
    if (result.validation.finalOutcome === "accepted") {
      summaryStrip.classList.add("success");
    } else {
      summaryStrip.classList.add("failure");
    }
  }

  setText(
    summaryStrip,
    `${outcomeLabel(result.validation.finalOutcome)}. ${result.scenario.expectedMessage}`
  );

  setText(
    tmSummary,
    result.validation.tmAccepted
      ? "TM normalization accepted the statement."
      : "TM normalization rejected the statement."
  );

  const providerAccepted = result.validation.providerAccepted;
  setText(
    providerSummary,
    providerAccepted === null || providerAccepted === undefined
      ? "Provider validation did not run because TM validation failed."
      : providerAccepted
        ? `Provider accepted the request using ${result.validation.providerDecision?.selectedProfile || "the matched profile"}.`
        : `Provider rejected the request${result.validation.providerDecision?.selectedProfile ? ` for ${result.validation.providerDecision.selectedProfile}` : ""}.`
  );

  renderIssues(tmIssues, result.validation.tmIssues || []);
  renderIssues(providerIssues, result.validation.providerDecision?.checks || []);
  setText(generatedTmFstar, generatedTmFStarText(result));
  setText(generatedProviderFstar, generatedProviderFStarText(result));
  setText(normalizedJson, formatJson(result.validation.normalizedIntent));
  setText(referenceFstar, result.scenario.referenceFStar || "No reference F* file is attached to this scenario.");
}

async function validateScenario() {
  const scenario = selectedScenario();
  if (!scenario) {
    return;
  }

  runButton.disabled = true;
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
  const ordered = featuredScenarioIds
    .map((id) => scenarios.find((scenario) => scenario.id === id))
    .filter(Boolean);

  scenariosById = new Map(ordered.map((scenario) => [scenario.id, scenario]));

  ordered.forEach((scenario) => {
    const option = document.createElement("option");
    option.value = scenario.id;
    option.textContent = scenarioTitles[scenario.id] || scenario.id;
    scenarioSelect?.appendChild(option);
  });

  if (ordered.length > 0 && scenarioSelect) {
    scenarioSelect.value = ordered[0].id;
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
    summaryStrip.className = "summary-strip failure";
    setText(summaryStrip, `Unable to load demo scenarios. ${error.message}`);
  }
}

bootstrap();
