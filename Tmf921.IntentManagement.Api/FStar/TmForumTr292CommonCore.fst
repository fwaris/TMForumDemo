module TmForumTr292CommonCore

open TR290A
open TR290V
open TR292A
open TR292C
open TR292D
open TR292E
open TR292F
open TR292G
open TR292I
open TR292Release3

(* Restricted executable projection of the TM Forum Intent Ontology release 3.
   This module keeps a stable entry point for the codebase while composing the
   common-core checks from the component ontologies that back TR292/TIO. *)

type intent_family =
  | BroadcastFamily
  | CriticalServiceFamily

type target_kind =
  | VenueTarget
  | FacilityTarget

type raw_tm_intent = {
  intent_name:string;
  scenario_family:intent_family;
  target_name:option string;
  target_kind:option target_kind;
  service_class:option string;
  event_month:option string;
  event_day:option nat;
  event_year:option nat;
  start_hour:option nat;
  end_hour:option nat;
  timezone:option string;
  primary_device_count:option nat;
  auxiliary_endpoint_count:option nat;
  max_latency_ms:option nat;
  reporting_interval_minutes:option nat;
  immediate_degradation_alerts:bool;
  safety_policy_declared:bool;
  preserve_emergency_traffic:bool;
  request_public_safety_preemption:bool
}

let common_core_release_id = tr292_release_id
let common_core_release_version = tr292_release_version

let target_descriptor_of (i:raw_tm_intent) : target_descriptor target_kind =
  { descriptor_name = i.target_name;
    descriptor_kind = i.target_kind }

let delivery_window_of (i:raw_tm_intent) : delivery_window =
  { window_month = i.event_month;
    window_day = i.event_day;
    window_year = i.event_year;
    window_start_hour = i.start_hour;
    window_end_hour = i.end_hour;
    window_timezone = i.timezone }

let reporting_expectation_of (i:raw_tm_intent) : reporting_expectation =
  { expected_interval_minutes = i.reporting_interval_minutes;
    requires_immediate_alerts = i.immediate_degradation_alerts }

let intent_expression_of (i:raw_tm_intent) : intent_expression_projection target_kind =
  { projection_target = target_descriptor_of i;
    projection_service_class = i.service_class;
    projection_delivery_window = delivery_window_of i;
    projection_reporting = reporting_expectation_of i }

let metric_requirements_of (i:raw_tm_intent) : metric_requirements =
  { metric_primary_device_count = i.primary_device_count;
    metric_auxiliary_endpoint_count = i.auxiliary_endpoint_count;
    metric_latency_bound_ms = i.max_latency_ms;
    metric_reporting_interval_minutes = i.reporting_interval_minutes }

let security_posture_of (i:raw_tm_intent) : security_posture =
  { declared_safety_policy = i.safety_policy_declared;
    preserves_emergency_traffic = i.preserve_emergency_traffic;
    requests_public_safety_preemption = i.request_public_safety_preemption }

let primary_device_quantity_of (i:raw_tm_intent) : quantity_requirement =
  { quantity_value = i.primary_device_count;
    quantity_unit = Some devices_unit }

let auxiliary_endpoint_quantity_of (i:raw_tm_intent) : quantity_requirement =
  { quantity_value = i.auxiliary_endpoint_count;
    quantity_unit = Some endpoints_unit }

let max_latency_quantity_of (i:raw_tm_intent) : quantity_requirement =
  { quantity_value = i.max_latency_ms;
    quantity_unit = Some milliseconds_unit }

let reporting_interval_quantity_of (i:raw_tm_intent) : quantity_requirement =
  { quantity_value = i.reporting_interval_minutes;
    quantity_unit = Some minutes_unit }

let requires_auxiliary_metric (i:raw_tm_intent) : bool =
  match i.scenario_family with
  | BroadcastFamily -> false
  | CriticalServiceFamily -> true

let measurable (i:raw_tm_intent) : bool =
  let expression = intent_expression_of i in
  let metrics = metric_requirements_of i in
  all_of
    (target_present expression.projection_target)
    (all_of
      (service_present expression.projection_service_class)
      (all_of
        (delivery_window_present expression.projection_delivery_window)
        (all_of
          (reporting_expectation_present expression.projection_reporting)
          (required_metrics_present (requires_auxiliary_metric i) metrics))))

let quantities_ok (i:raw_tm_intent) : bool =
  all_quantities_positive
    (requires_auxiliary_metric i)
    (primary_device_quantity_of i)
    (auxiliary_endpoint_quantity_of i)
    (max_latency_quantity_of i)
    (reporting_interval_quantity_of i)

let common_core_valid (i:raw_tm_intent) : bool =
  measurable i &&
  quantities_ok i

type measurable_intent (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ measurable v }

let mk_measurable
  (i:raw_tm_intent{ measurable i })
  : measurable_intent i =
  i

type quantity_checked_intent (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ measurable v /\ quantities_ok v }

let mk_quantity_checked
  (i:raw_tm_intent{ common_core_valid i })
  : quantity_checked_intent i =
  i
