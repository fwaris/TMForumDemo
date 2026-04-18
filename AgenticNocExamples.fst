module AgenticNocExamples

open AgenticNoc

let evidence_good : evidence =
  {
    alarms_seen = true;
    packet_loss_high = true;
    kpi_degraded = true;
    redundancy_available = true;
    sla_guard_ok = true;
    diagnosis_ready = true
  }

let approval_ops : approval_token =
  {
    approver = "noc-supervisor";
    reason = "Critical incident with human authorization."
  }

let synthesized_restart_checks : bool =
  match parse_intent sample_text_restart with
  | NeedsClarification _ -> false
  | Parsed ast ->
    match validate_intent ast with
    | None -> false
    | Some checked ->
      match check_plan (synthesize_plan checked) with
      | Ok _ -> true
      | Error _ -> false

let _ : unit = assert synthesized_restart_checks

let ticket_triage_checks : bool =
  match parse_intent sample_text_ticket with
  | NeedsClarification _ -> false
  | Parsed ast ->
    match validate_intent ast with
    | None -> false
    | Some checked ->
      match check_plan (synthesize_plan checked) with
      | Ok _ -> true
      | Error _ -> false

let _ : unit = assert ticket_triage_checks

let conflicting_intent_rejected : bool =
  match parse_intent sample_text_conflict with
  | NeedsClarification _ -> false
  | Parsed ast ->
    match validate_intent ast with
    | None -> true
    | Some _ -> false

let _ : unit = assert conflicting_intent_rejected

let unauthorized_enforce_rejected : bool =
  match parse_intent sample_text_ticket with
  | NeedsClarification _ -> false
  | Parsed ast ->
    match validate_intent ast with
    | None -> false
    | Some checked ->
      let observer : actor ObserveOnly = { actor_name = "observer-agent" } in
      let spec : plan_spec ObserveOnly =
        {
          actor = observer;
          intent = checked;
          evidence = evidence_good;
          assumptions = ["Observer is limited to analysis only."];
          steps = [
            ObserveAlarm ast.target_scope;
            EnforceRestart ast.target_scope
          ];
          approvals = [approval_ops]
        } in
      match check_plan (SomePlanSpec spec) with
      | Error (UnauthorizedAction _) -> true
      | _ -> false

let _ : unit = assert unauthorized_enforce_rejected

let missing_approval_rejected : bool =
  match parse_intent sample_text_isolate with
  | NeedsClarification _ -> false
  | Parsed ast ->
    match validate_intent ast with
    | None -> false
    | Some checked ->
      let operator : actor ActCapable = { actor_name = "operator-agent" } in
      let spec : plan_spec ActCapable =
        {
          actor = operator;
          intent = checked;
          evidence = evidence_good;
          assumptions = ["High-risk action proposed directly."];
          steps = [
            IsolateFailedNode ast.target_scope;
            RollbackIsolation ast.target_scope
          ];
          approvals = []
        } in
      match check_plan (SomePlanSpec spec) with
      | Error (MissingApproval _) -> true
      | _ -> false

let _ : unit = assert missing_approval_rejected

let missing_rollback_rejected : bool =
  match parse_intent sample_text_isolate with
  | NeedsClarification _ -> false
  | Parsed ast ->
    match validate_intent ast with
    | None -> false
    | Some checked ->
      let operator : actor ActCapable = { actor_name = "operator-agent" } in
      let spec : plan_spec ActCapable =
        {
          actor = operator;
          intent = checked;
          evidence = evidence_good;
          assumptions = ["Rollback was accidentally omitted."];
          steps = [IsolateFailedNode ast.target_scope];
          approvals = [approval_ops]
        } in
      match check_plan (SomePlanSpec spec) with
      | Error (MissingRollback _) -> true
      | _ -> false

let _ : unit = assert missing_rollback_rejected

let insufficient_evidence_rejected : bool =
  match parse_intent sample_text_restart with
  | NeedsClarification _ -> false
  | Parsed ast ->
    match validate_intent ast with
    | None -> false
    | Some checked ->
      let operator : actor ActCapable = { actor_name = "operator-agent" } in
      let weak_evidence =
        {
          alarms_seen = true;
          packet_loss_high = false;
          kpi_degraded = false;
          redundancy_available = true;
          sla_guard_ok = true;
          diagnosis_ready = false
        } in
      let spec : plan_spec ActCapable =
        {
          actor = operator;
          intent = checked;
          evidence = weak_evidence;
          assumptions = ["Telemetry normalization did not establish diagnosis."];
          steps = [EnforceRestart ast.target_scope];
          approvals = [approval_ops]
        } in
      match check_plan (SomePlanSpec spec) with
      | Error (InsufficientEvidence _) -> true
      | _ -> false

let _ : unit = assert insufficient_evidence_rejected
