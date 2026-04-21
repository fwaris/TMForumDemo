module CriticalServiceDemo.FailTm

open TmForumTr292CommonCore

let candidate_intent : raw_tm_intent =
  { intent_name = "CriticalServiceIntent";
    scenario_family = CriticalServiceFamily;
    target_name = None;
    target_kind = None;
    service_class = None;
    event_month = None;
    event_day = None;
    event_year = None;
    start_hour = None;
    end_hour = None;
    timezone = None;
    primary_device_count = None;
    auxiliary_endpoint_count = None;
    max_latency_ms = None;
    reporting_interval_minutes = None;
    immediate_degradation_alerts = false;
    safety_policy_declared = false;
    preserve_emergency_traffic = false;
    request_public_safety_preemption = false }

let measurable_witness : measurable_intent candidate_intent =
  mk_measurable candidate_intent
