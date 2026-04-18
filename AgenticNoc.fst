module AgenticNoc

type result (a:Type) (e:Type) =
  | Ok : a -> result a e
  | Error : e -> result a e

type domain =
  | RAN
  | Transport
  | Core
  | FieldForce
  | Security
  | Analytics

type mode =
  | Observe
  | Propose
  | Simulate
  | Enforce

type risk =
  | Low
  | Medium
  | High

type goal =
  | IncidentRemediation
  | IncidentTriage
  | TicketEnrichment

type urgency =
  | Routine
  | Elevated
  | Critical

type autonomy =
  | HumanInLoop
  | Assisted
  | Autonomous

type capability =
  | ObserveOnly
  | ActCapable

type target_scope = {
  region:string;
  node:string
}

let string_non_empty (s:string) : bool = s <> ""

let scope_non_empty (s:target_scope) : bool =
  string_non_empty s.region || string_non_empty s.node

let scope_eq (x:target_scope) (y:target_scope) : bool =
  x.region = y.region && x.node = y.node

type constraint_atom =
  | PreserveSLA
  | NoTrafficReroute
  | ApprovalRequired
  | RedundancyRequired
  | RestoreServiceImmediately
  | ReadOnlyOnly

type preference =
  | MinimizeCustomerImpact
  | MinimizeTimeToRestore
  | MinimizeEnergy
  | PreferHumanReview

type intent_ast = {
  goal:goal;
  target_scope:target_scope;
  constraints:list constraint_atom;
  preferences:list preference;
  urgency:urgency;
  requested_autonomy:autonomy
}

type intent_text = string

type parsed_intent_result =
  | Parsed : intent_ast -> parsed_intent_result
  | NeedsClarification : string -> parsed_intent_result

let rec has_constraint (needle:constraint_atom) (haystack:list constraint_atom) : bool =
  match haystack with
  | [] -> false
  | hd::tl -> if hd = needle then true else has_constraint needle tl

let constraints_consistent (cs:list constraint_atom) : bool =
  not (has_constraint RestoreServiceImmediately cs && has_constraint NoTrafficReroute cs)

type checked_intent (ast:intent_ast) =
  normalized:intent_ast{
    normalized == ast /\
    scope_non_empty normalized.target_scope &&
    constraints_consistent normalized.constraints
  }

type some_checked_intent =
  | CheckedIntent : #ast:intent_ast -> checked_intent ast -> some_checked_intent

let mk_checked_intent
  (ast:intent_ast{
    scope_non_empty ast.target_scope &&
    constraints_consistent ast.constraints
  })
  : checked_intent ast =
  ast

type actor (c:capability) = {
  actor_name:string
}

type approval_token = {
  approver:string;
  reason:string
}

type evidence = {
  alarms_seen:bool;
  packet_loss_high:bool;
  kpi_degraded:bool;
  redundancy_available:bool;
  sla_guard_ok:bool;
  diagnosis_ready:bool
}

type plan_atom =
  | ObserveAlarm : target_scope -> plan_atom
  | CorrelateIncident : target_scope -> plan_atom
  | OpenPriorityIncident : target_scope -> urgency -> plan_atom
  | ProposeRestart : target_scope -> plan_atom
  | EnforceRestart : target_scope -> plan_atom
  | IsolateFailedNode : target_scope -> plan_atom
  | EscalateToHuman : string -> plan_atom
  | RollbackIsolation : target_scope -> plan_atom

let domain_of (p:plan_atom) : domain =
  match p with
  | ObserveAlarm _ -> RAN
  | CorrelateIncident _ -> Analytics
  | OpenPriorityIncident _ _ -> Analytics
  | ProposeRestart _ -> RAN
  | EnforceRestart _ -> RAN
  | IsolateFailedNode _ -> Core
  | EscalateToHuman _ -> Analytics
  | RollbackIsolation _ -> Core

let mode_of (p:plan_atom) : mode =
  match p with
  | ObserveAlarm _ -> Observe
  | CorrelateIncident _ -> Propose
  | OpenPriorityIncident _ _ -> Propose
  | ProposeRestart _ -> Propose
  | EnforceRestart _ -> Enforce
  | IsolateFailedNode _ -> Enforce
  | EscalateToHuman _ -> Propose
  | RollbackIsolation _ -> Enforce

let risk_of (p:plan_atom) : risk =
  match p with
  | ObserveAlarm _ -> Low
  | CorrelateIncident _ -> Low
  | OpenPriorityIncident _ _ -> Low
  | ProposeRestart _ -> Medium
  | EnforceRestart _ -> Medium
  | IsolateFailedNode _ -> High
  | EscalateToHuman _ -> Low
  | RollbackIsolation _ -> Medium

let mode_allowed (c:capability) (m:mode) : bool =
  match c, m with
  | ObserveOnly, Enforce -> false
  | _, _ -> true

let requires_approval (p:plan_atom) : bool =
  mode_of p = Enforce && risk_of p <> Low

let rollback_required (p:plan_atom) : bool =
  match p with
  | IsolateFailedNode _ -> true
  | _ -> false

let preserves_slo_step (ev:evidence) (p:plan_atom) : bool =
  match mode_of p with
  | Enforce -> ev.sla_guard_ok
  | _ -> true

let evidence_sufficient (ev:evidence) (p:plan_atom) : bool =
  match p with
  | ObserveAlarm _ -> true
  | CorrelateIncident _ -> ev.alarms_seen
  | OpenPriorityIncident _ _ -> ev.diagnosis_ready
  | ProposeRestart _ -> ev.diagnosis_ready && ev.packet_loss_high && ev.kpi_degraded
  | EnforceRestart _ -> ev.diagnosis_ready && ev.packet_loss_high
  | IsolateFailedNode _ -> ev.diagnosis_ready && ev.redundancy_available
  | EscalateToHuman _ -> true
  | RollbackIsolation _ -> ev.sla_guard_ok

let safe_under (cs:list constraint_atom) (ev:evidence) (p:plan_atom) : bool =
  evidence_sufficient ev p &&
  preserves_slo_step ev p &&
  (if has_constraint ReadOnlyOnly cs then mode_of p <> Enforce else true) &&
  (if has_constraint RedundancyRequired cs
   then match p with
        | IsolateFailedNode _ -> ev.redundancy_available
        | _ -> true
   else true) &&
  (if has_constraint NoTrafficReroute cs
   then match p with
        | IsolateFailedNode _ -> ev.redundancy_available && ev.sla_guard_ok
        | _ -> true
   else true)

let has_any_approval (approvals:list approval_token) : bool =
  match approvals with
  | [] -> false
  | _ -> true

let approval_satisfied (approvals:list approval_token) (p:plan_atom) : bool =
  if requires_approval p then has_any_approval approvals else true

let rec contains_rollback_for (steps:list plan_atom) (p:plan_atom) : bool =
  match steps with
  | [] -> false
  | hd::tl ->
    match p, hd with
    | IsolateFailedNode s1, RollbackIsolation s2 -> scope_eq s1 s2 || contains_rollback_for tl p
    | _, _ -> contains_rollback_for tl p

let rollback_available (steps:list plan_atom) (p:plan_atom) : bool =
  if rollback_required p then contains_rollback_for steps p else true

let rec preserves_slo_plan (ev:evidence) (steps:list plan_atom) : bool =
  match steps with
  | [] -> true
  | hd::tl -> preserves_slo_step ev hd && preserves_slo_plan ev tl

let rec plan_ok
  (c:capability)
  (cs:list constraint_atom)
  (ev:evidence)
  (approvals:list approval_token)
  (steps:list plan_atom)
  : bool =
  match steps with
  | [] -> true
  | hd::tl ->
    mode_allowed c (mode_of hd) &&
    safe_under cs ev hd &&
    approval_satisfied approvals hd &&
    rollback_available steps hd &&
    plan_ok c cs ev approvals tl

type plan_spec (c:capability) = {
  actor:actor c;
  intent:some_checked_intent;
  evidence:evidence;
  assumptions:list string;
  steps:list plan_atom;
  approvals:list approval_token
}

type some_plan_spec =
  | SomePlanSpec : #c:capability -> plan_spec c -> some_plan_spec

type check_error =
  | EmptyPlan
  | UnauthorizedAction : plan_atom -> check_error
  | InsufficientEvidence : plan_atom -> check_error
  | UnsafeAction : plan_atom -> check_error
  | MissingApproval : plan_atom -> check_error
  | MissingRollback : plan_atom -> check_error

let checked_constraints (i:some_checked_intent) : list constraint_atom =
  match i with
  | CheckedIntent ci -> ci.constraints

type executable_plan (c:capability) = spec:plan_spec c{
  spec.steps <> [] &&
  plan_ok c (checked_constraints spec.intent) spec.evidence spec.approvals spec.steps &&
  preserves_slo_plan spec.evidence spec.steps
}

type some_executable_plan =
  | SomeExecutablePlan : #c:capability -> executable_plan c -> some_executable_plan

let sample_text_restart : intent_text =
  "If sector KPIs degrade and packet loss exceeds threshold, propose restart but do not execute without approval."

let sample_text_ticket : intent_text =
  "Correlate alarms in region X and open a priority-1 incident with recommended remediation."

let sample_text_isolate : intent_text =
  "Isolate the failed node if redundancy is available and customer-impact SLA remains satisfied."

let sample_text_conflict : intent_text =
  "Restore service immediately with no traffic reroute."

let parse_intent (txt:intent_text) : Tot parsed_intent_result =
  if txt = sample_text_restart then
    Parsed {
      goal = IncidentRemediation;
      target_scope = { region = "region-x"; node = "sector-7" };
      constraints = [ApprovalRequired; PreserveSLA];
      preferences = [MinimizeTimeToRestore; PreferHumanReview];
      urgency = Critical;
      requested_autonomy = HumanInLoop
    }
  else if txt = sample_text_ticket then
    Parsed {
      goal = IncidentTriage;
      target_scope = { region = "region-x"; node = "" };
      constraints = [PreserveSLA];
      preferences = [MinimizeCustomerImpact];
      urgency = Critical;
      requested_autonomy = Assisted
    }
  else if txt = sample_text_isolate then
    Parsed {
      goal = IncidentRemediation;
      target_scope = { region = "region-x"; node = "core-node-1" };
      constraints = [PreserveSLA; RedundancyRequired; ApprovalRequired];
      preferences = [MinimizeCustomerImpact];
      urgency = Critical;
      requested_autonomy = HumanInLoop
    }
  else if txt = sample_text_conflict then
    Parsed {
      goal = IncidentRemediation;
      target_scope = { region = "region-x"; node = "core-node-2" };
      constraints = [RestoreServiceImmediately; NoTrafficReroute];
      preferences = [MinimizeTimeToRestore];
      urgency = Critical;
      requested_autonomy = Autonomous
    }
  else
    NeedsClarification "Unable to normalize the text into the controlled incident-remediation DSL."

let validate_intent (ast:intent_ast) : Tot (option some_checked_intent) =
  if scope_non_empty ast.target_scope && constraints_consistent ast.constraints
  then Some (CheckedIntent (mk_checked_intent ast))
  else None

let default_evidence (ci:some_checked_intent) : evidence =
  match ci with
  | CheckedIntent checked ->
    match checked.goal with
    | IncidentRemediation ->
      {
        alarms_seen = true;
        packet_loss_high = true;
        kpi_degraded = true;
        redundancy_available = true;
        sla_guard_ok = true;
        diagnosis_ready = true
      }
    | IncidentTriage
    | TicketEnrichment ->
      {
        alarms_seen = true;
        packet_loss_high = false;
        kpi_degraded = false;
        redundancy_available = false;
        sla_guard_ok = true;
        diagnosis_ready = true
      }

let synthesize_steps (ci:some_checked_intent) : list plan_atom =
  match ci with
  | CheckedIntent checked ->
    let ast = checked in
    match ast.goal with
    | IncidentTriage ->
      [
        ObserveAlarm ast.target_scope;
        CorrelateIncident ast.target_scope;
        OpenPriorityIncident ast.target_scope ast.urgency
      ]
    | TicketEnrichment ->
      [
        CorrelateIncident ast.target_scope;
        OpenPriorityIncident ast.target_scope ast.urgency
      ]
    | IncidentRemediation ->
      if has_constraint ApprovalRequired ast.constraints || ast.requested_autonomy = HumanInLoop
      then [
        ObserveAlarm ast.target_scope;
        CorrelateIncident ast.target_scope;
        OpenPriorityIncident ast.target_scope ast.urgency;
        ProposeRestart ast.target_scope;
        EscalateToHuman "Approval required before any enforce action."
      ]
      else [
        ObserveAlarm ast.target_scope;
        CorrelateIncident ast.target_scope;
        OpenPriorityIncident ast.target_scope ast.urgency;
        EnforceRestart ast.target_scope
      ]

let synthesize_plan (ci:some_checked_intent) : Tot some_plan_spec =
  let actor : actor ActCapable = { actor_name = "llm-orchestrator" } in
  SomePlanSpec {
    actor = actor;
    intent = ci;
    evidence = default_evidence ci;
    assumptions = ["Evidence was normalized from telemetry before planning."];
    steps = synthesize_steps ci;
    approvals = []
  }

let rec first_error
  (c:capability)
  (cs:list constraint_atom)
  (ev:evidence)
  (approvals:list approval_token)
  (all_steps:list plan_atom)
  (remaining:list plan_atom)
  : Tot (option check_error) =
  match remaining with
  | [] -> None
  | hd::tl ->
    if not (mode_allowed c (mode_of hd)) then Some (UnauthorizedAction hd)
    else if not (evidence_sufficient ev hd) then Some (InsufficientEvidence hd)
    else if not (safe_under cs ev hd) then Some (UnsafeAction hd)
    else if not (approval_satisfied approvals hd) then Some (MissingApproval hd)
    else if not (rollback_available all_steps hd) then Some (MissingRollback hd)
    else first_error c cs ev approvals all_steps tl

let check_plan (sp:some_plan_spec) : Tot (result some_executable_plan check_error) =
  match sp with
  | SomePlanSpec #c spec ->
    if spec.steps = [] then Error EmptyPlan
    else match first_error c (checked_constraints spec.intent) spec.evidence spec.approvals spec.steps spec.steps with
         | Some err -> Error err
         | None ->
           if plan_ok c (checked_constraints spec.intent) spec.evidence spec.approvals spec.steps &&
              preserves_slo_plan spec.evidence spec.steps
           then Ok (SomeExecutablePlan spec)
           else Error EmptyPlan
