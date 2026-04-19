module BroadcastProviderDemo.FailCapacity

open BroadcastProviderDemo

let overloaded_intent : tm_intent =
  { intent_name = "DetroitStadiumBroadcastCapacityFailure";
    venue = Some DetroitStadium;
    service_class = Some "premium-5g-broadcast";
    event_month = Some "April";
    event_day = Some 25;
    event_year = Some 2026;
    start_hour = Some 18;
    end_hour = Some 22;
    timezone = Some "America/Detroit";
    device_count = Some 2000;
    max_uplink_latency_ms = Some 20;
    reporting_interval_minutes = Some 60;
    immediate_degradation_alerts = true;
    preserve_emergency_traffic = true;
    request_public_safety_preemption = false }

let selected_profile : profile =
  LiveBroadcastGold

let measurable : measurable_intent overloaded_intent =
  mk_measurable overloaded_intent

let window_checked : window_checked_intent overloaded_intent =
  mk_window_checked overloaded_intent

let tm_checked : tm_checked_intent overloaded_intent =
  mk_tm_checked overloaded_intent

let profiled : profiled_intent selected_profile overloaded_intent =
  mk_profiled selected_profile overloaded_intent

let capacity_checked : capacity_checked_intent selected_profile overloaded_intent =
  mk_capacity_checked selected_profile overloaded_intent
