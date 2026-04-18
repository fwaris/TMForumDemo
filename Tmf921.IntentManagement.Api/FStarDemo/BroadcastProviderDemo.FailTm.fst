module BroadcastProviderDemo.FailTm

open BroadcastProviderDemo

let vague_intent : tm_intent =
  { intent_name = "VagueBroadcastIntent";
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

let tm_checked : tm_checked_intent vague_intent =
  mk_tm_checked vague_intent
