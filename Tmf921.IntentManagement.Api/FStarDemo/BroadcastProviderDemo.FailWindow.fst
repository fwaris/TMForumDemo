module BroadcastProviderDemo.FailWindow

open BroadcastProviderDemo

let reversed_window_intent : tm_intent =
  { intent_name = "DetroitStadiumBroadcastWindowFailure";
    venue = Some DetroitStadium;
    service_class = Some "premium-5g-broadcast";
    event_month = Some "April";
    event_day = Some 25;
    event_year = Some 2026;
    start_hour = Some 22;
    end_hour = Some 18;
    timezone = Some "America/Detroit";
    device_count = Some 200;
    max_uplink_latency_ms = Some 20;
    reporting_interval_minutes = Some 60;
    immediate_degradation_alerts = true;
    preserve_emergency_traffic = true;
    request_public_safety_preemption = false }

let measurable : measurable_intent reversed_window_intent =
  mk_measurable reversed_window_intent

let window_checked : window_checked_intent reversed_window_intent =
  mk_window_checked reversed_window_intent
