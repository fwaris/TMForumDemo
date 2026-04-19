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

const grid = document.getElementById("scenario-grid");
const template = document.getElementById("scenario-template");

function formatJson(value) {
  return value ? JSON.stringify(value, null, 2) : "No normalized structure was produced.";
}

function setText(element, text) {
  element.textContent = text;
}

function renderIssues(listElement, issues) {
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

function generatedFStarText(result) {
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

function applyResult(card, result) {
  const summary = card.querySelector(".summary-strip");
  const tmSummary = card.querySelector(".tm-summary");
  const providerSummary = card.querySelector(".provider-summary");
  const normalizedJson = card.querySelector(".normalized-json");
  const generatedFstar = card.querySelector(".generated-fstar");
  const referenceFstar = card.querySelector(".reference-fstar");

  summary.className = "summary-strip";
  if (result.validation.finalOutcome === "accepted") {
    summary.classList.add("success");
  } else {
    summary.classList.add("failure");
  }

  setText(
    summary,
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

  renderIssues(card.querySelector(".tm-issues"), result.validation.tmIssues || []);
  renderIssues(card.querySelector(".provider-issues"), result.validation.providerDecision?.checks || []);
  setText(normalizedJson, formatJson(result.validation.normalizedIntent));
  setText(generatedFstar, generatedFStarText(result));
  setText(referenceFstar, result.scenario.referenceFStar || "No reference F* file is attached to this scenario.");
}

async function validateScenario(card, scenarioId, text) {
  const button = card.querySelector(".run-button");
  const state = card.querySelector(".run-state");
  button.disabled = true;
  setText(state, "Running backend validation...");

  try {
    const response = await fetch("/tmf-api/intentManagement/v5/demo/validate", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({ scenarioId, text })
    });

    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}`);
    }

    const result = await response.json();
    applyResult(card, result);
    setText(state, "Validation complete");
  } catch (error) {
    const summary = card.querySelector(".summary-strip");
    summary.className = "summary-strip failure";
    setText(summary, `Validation request failed. ${error.message}`);
    setText(state, "Backend request failed");
  } finally {
    button.disabled = false;
  }
}

function buildCard(scenario) {
  const fragment = template.content.cloneNode(true);
  const card = fragment.querySelector(".scenario-card");
  const input = fragment.querySelector(".scenario-input");
  const button = fragment.querySelector(".run-button");

  setText(fragment.querySelector(".scenario-kicker"), scenarioKickers[scenario.id] || "Scenario");
  setText(fragment.querySelector(".scenario-title"), scenarioTitles[scenario.id] || scenario.id);
  setText(
    fragment.querySelector(".scenario-badge"),
    expectedLabels[scenario.expectedOutcome] || scenario.expectedOutcome
  );
  input.value = scenario.text;

  button.addEventListener("click", () => validateScenario(card, scenario.id, input.value));
  return fragment;
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

  ordered.forEach((scenario) => {
    grid.appendChild(buildCard(scenario));
  });

  document.querySelectorAll(".scenario-card").forEach((card) => {
    const button = card.querySelector(".run-button");
    button.click();
  });
}

async function bootstrap() {
  await loadHealth();

  try {
    await loadScenarios();
  } catch (error) {
    const fallback = document.createElement("article");
    fallback.className = "scenario-card";
    fallback.innerHTML = `<p>Unable to load demo scenarios. ${error.message}</p>`;
    grid.appendChild(fallback);
  }
}

bootstrap();
