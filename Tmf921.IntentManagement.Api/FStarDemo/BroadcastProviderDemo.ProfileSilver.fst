module BroadcastProviderDemo.ProfileSilver

open BroadcastProviderDemo

let silver_profile_intent : tm_intent =
  { intent_name = "MetroArenaProfileSilver";
    venue = Some MetroArena;
    service_class = Some "premium-5g-broadcast";
    event_month = Some "April";
    event_day = Some 25;
    event_year = Some 2026;
    start_hour = Some 18;
    end_hour = Some 22;
    timezone = Some "America/Detroit";
    device_count = Some 90;
    max_uplink_latency_ms = Some 30;
    reporting_interval_minutes = Some 60;
    immediate_degradation_alerts = true;
    preserve_emergency_traffic = true;
    request_public_safety_preemption = false }

let selected_profile : profile =
  LiveBroadcastSilver

let measurable : measurable_intent silver_profile_intent =
  mk_measurable silver_profile_intent

let quantity_checked : quantity_checked_intent silver_profile_intent =
  mk_quantity_checked silver_profile_intent

let window_checked : window_checked_intent silver_profile_intent =
  mk_window_checked silver_profile_intent

let profiled : profiled_intent selected_profile silver_profile_intent =
  mk_profiled selected_profile silver_profile_intent

let capacity_checked : capacity_checked_intent selected_profile silver_profile_intent =
  mk_capacity_checked selected_profile silver_profile_intent

let latency_checked : latency_checked_intent selected_profile silver_profile_intent =
  mk_latency_checked selected_profile silver_profile_intent
