module BroadcastProviderDemo.Success

open BroadcastProviderDemo

let success_intent : tm_intent =
  { intent_name = "DetroitStadiumLiveBroadcast";
    venue = Some DetroitStadium;
    service_class = Some "premium-5g-broadcast";
    event_month = Some "April";
    event_day = Some 25;
    event_year = Some 2026;
    start_hour = Some 18;
    end_hour = Some 22;
    timezone = Some "America/Detroit";
    device_count = Some 200;
    max_uplink_latency_ms = Some 20;
    reporting_interval_minutes = Some 60;
    immediate_degradation_alerts = true;
    preserve_emergency_traffic = true;
    request_public_safety_preemption = false }

let selected_profile : profile =
  LiveBroadcastGold

let measurable : measurable_intent success_intent =
  mk_measurable success_intent

let quantity_checked : quantity_checked_intent success_intent =
  mk_quantity_checked success_intent

let window_checked : window_checked_intent success_intent =
  mk_window_checked success_intent

let profiled : profiled_intent selected_profile success_intent =
  mk_profiled selected_profile success_intent

let capacity_checked : capacity_checked_intent selected_profile success_intent =
  mk_capacity_checked selected_profile success_intent

let latency_checked : latency_checked_intent selected_profile success_intent =
  mk_latency_checked selected_profile success_intent

let policy_checked : policy_checked_intent selected_profile success_intent =
  mk_policy_checked selected_profile success_intent

let provider_checked : provider_checked_intent selected_profile success_intent =
  mk_provider_checked selected_profile success_intent

let admission_token_for_demo : admission_token selected_profile =
  issue_admission_token selected_profile success_intent provider_checked
