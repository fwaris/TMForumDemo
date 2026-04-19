module BroadcastProviderDemo.Generatedbroadcast_fail_tm_01

open BroadcastProviderDemo

let demo_intent : tm_intent =
  { intent_name = "LiveBroadcastIntent";
    venue = None;
    service_class = None;
    event_month = None;
    event_day = None;
    event_year = None;
    start_hour = None;
    end_hour = None;
    timezone = None;
    device_count = None;
    max_uplink_latency_ms = None;
    reporting_interval_minutes = None;
    immediate_degradation_alerts = false;
    preserve_emergency_traffic = false;
    request_public_safety_preemption = false }

let selected_profile : profile =
  UnsupportedProfile

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
