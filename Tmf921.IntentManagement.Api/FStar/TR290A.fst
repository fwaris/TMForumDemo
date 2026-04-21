module TR290A

let specification_id = "TR290A"
let specification_title = "Intent Common Model - Intent Expression"
let specification_version = "3.6.0"

let has_value (#a:Type) (value:option a) : bool =
  match value with
  | Some _ -> true
  | None -> false

type target_descriptor (a:Type) = {
  descriptor_name:option string;
  descriptor_kind:option a
}

type delivery_window = {
  window_month:option string;
  window_day:option nat;
  window_year:option nat;
  window_start_hour:option nat;
  window_end_hour:option nat;
  window_timezone:option string
}

type reporting_expectation = {
  expected_interval_minutes:option nat;
  requires_immediate_alerts:bool
}

type intent_expression_projection (a:Type) = {
  projection_target:target_descriptor a;
  projection_service_class:option string;
  projection_delivery_window:delivery_window;
  projection_reporting:reporting_expectation
}

let target_present (#a:Type) (target:target_descriptor a) : bool =
  has_value target.descriptor_name &&
  has_value target.descriptor_kind

let delivery_window_present (window:delivery_window) : bool =
  has_value window.window_month &&
  has_value window.window_day &&
  has_value window.window_year &&
  has_value window.window_start_hour &&
  has_value window.window_end_hour &&
  has_value window.window_timezone

let service_present (service_class:option string) : bool =
  has_value service_class

let reporting_expectation_present (reporting:reporting_expectation) : bool =
  has_value reporting.expected_interval_minutes
