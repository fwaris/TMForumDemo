module BroadcastProviderDemo.Generatedbroadcast_fail_policy_01

open BroadcastProviderDemo

let demo_intent : tm_intent =
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
    preserve_emergency_traffic = false;
    request_public_safety_preemption = true }

let selected_profile : profile =
  LiveBroadcastGold

let measurable : measurable_intent demo_intent =
  mk_measurable demo_intent

let window_checked : window_checked_intent demo_intent =
  mk_window_checked demo_intent

let tm_checked : tm_checked_intent demo_intent =
  mk_tm_checked demo_intent

let profiled : profiled_intent selected_profile demo_intent =
  mk_profiled selected_profile demo_intent

let capacity_checked : capacity_checked_intent selected_profile demo_intent =
  mk_capacity_checked selected_profile demo_intent

let latency_checked : latency_checked_intent selected_profile demo_intent =
  mk_latency_checked selected_profile demo_intent

let policy_checked : policy_checked_intent selected_profile demo_intent =
  mk_policy_checked selected_profile demo_intent

let provider_checked : provider_checked_intent selected_profile demo_intent =
  mk_provider_checked selected_profile demo_intent

let admission_token_for_demo : admission_token selected_profile =
  issue_admission_token selected_profile demo_intent provider_checked
