module BroadcastProviderDemo.FailPolicy

open BroadcastProviderDemo

let unsafe_policy_intent : tm_intent =
  { intent_name = "DetroitStadiumBroadcastPolicyFailure";
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
    preserve_emergency_traffic = false;
    request_public_safety_preemption = true }

let selected_profile : profile =
  LiveBroadcastGold

let measurable : measurable_intent unsafe_policy_intent =
  mk_measurable unsafe_policy_intent

let quantity_checked : quantity_checked_intent unsafe_policy_intent =
  mk_quantity_checked unsafe_policy_intent

let window_checked : window_checked_intent unsafe_policy_intent =
  mk_window_checked unsafe_policy_intent

let profiled : profiled_intent selected_profile unsafe_policy_intent =
  mk_profiled selected_profile unsafe_policy_intent

let capacity_checked : capacity_checked_intent selected_profile unsafe_policy_intent =
  mk_capacity_checked selected_profile unsafe_policy_intent

let latency_checked : latency_checked_intent selected_profile unsafe_policy_intent =
  mk_latency_checked selected_profile unsafe_policy_intent

let policy_checked : policy_checked_intent selected_profile unsafe_policy_intent =
  mk_policy_checked selected_profile unsafe_policy_intent
