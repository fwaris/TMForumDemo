module BroadcastProviderDemo.FailWindow

open ProviderIntentAdmission
open TmForumTr292CommonCore

let reversed_window_intent : raw_tm_intent =
  { intent_name = "DetroitStadiumBroadcastWindowFailure";
    scenario_family = BroadcastFamily;
    target_name = Some "Detroit Stadium";
    target_kind = Some VenueTarget;
    service_class = Some "premium-5g-broadcast";
    event_month = Some "April";
    event_day = Some 25;
    event_year = Some 2026;
    start_hour = Some 22;
    end_hour = Some 18;
    timezone = Some "America/Detroit";
    primary_device_count = Some 200;
    auxiliary_endpoint_count = None;
    max_latency_ms = Some 20;
    reporting_interval_minutes = Some 60;
    immediate_degradation_alerts = true;
    safety_policy_declared = true;
    preserve_emergency_traffic = true;
    request_public_safety_preemption = false }

let measurable : measurable_intent reversed_window_intent =
  mk_measurable reversed_window_intent

let quantity_checked : quantity_checked_intent reversed_window_intent =
  mk_quantity_checked reversed_window_intent

let window_checked : window_checked_intent reversed_window_intent =
  mk_window_checked reversed_window_intent
