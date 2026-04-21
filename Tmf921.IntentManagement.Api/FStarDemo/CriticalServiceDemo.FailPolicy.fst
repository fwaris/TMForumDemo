module CriticalServiceDemo.FailPolicy

open ProviderIntentAdmission
open TmForumTr292CommonCore

let candidate_intent : raw_tm_intent =
  { intent_name = "CriticalServiceIntent";
    scenario_family = CriticalServiceFamily;
    target_name = Some "Mayo Clinic";
    target_kind = Some FacilityTarget;
    service_class = Some "ultra-reliable-5g-clinical";
    event_month = Some "April";
    event_day = Some 25;
    event_year = Some 2026;
    start_hour = Some 8;
    end_hour = Some 20;
    timezone = Some "America/Detroit";
    primary_device_count = Some 80;
    auxiliary_endpoint_count = Some 200;
    max_latency_ms = Some 10;
    reporting_interval_minutes = Some 5;
    immediate_degradation_alerts = true;
    safety_policy_declared = true;
    preserve_emergency_traffic = false;
    request_public_safety_preemption = true }

let measurable_witness : measurable_intent candidate_intent =
  mk_measurable candidate_intent

let quantity_witness : quantity_checked_intent candidate_intent =
  mk_quantity_checked candidate_intent

let window_witness : window_checked_intent candidate_intent =
  mk_window_checked candidate_intent

let selected_profile : profile =
  resolve_profile candidate_intent

let profiled_witness : profiled_intent selected_profile candidate_intent =
  mk_profiled selected_profile candidate_intent

let capacity_witness : capacity_checked_intent selected_profile candidate_intent =
  mk_capacity_checked selected_profile candidate_intent

let latency_witness : latency_checked_intent selected_profile candidate_intent =
  mk_latency_checked selected_profile candidate_intent

let policy_witness : policy_checked_intent selected_profile candidate_intent =
  mk_policy_checked selected_profile candidate_intent
