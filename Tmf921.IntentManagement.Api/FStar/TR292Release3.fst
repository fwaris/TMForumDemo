module TR292Release3

type release_component_role =
  | MandatoryCommonModel
  | BaseOntologyModel
  | ExtensionModel
  | IntentSpecification
  | SupportingReference

type specification_reference = {
  spec_id:string;
  spec_title:string;
  spec_version:string;
  spec_role:release_component_role
}

let tr292_release_id = "TR292"
let tr292_release_title = "TM Forum Intent Ontology (TIO)"
let tr292_release_version = "3.6.0"

let tr290a_reference : specification_reference =
  { spec_id = "TR290A";
    spec_title = "Intent Common Model - Intent Expression";
    spec_version = "3.6.0";
    spec_role = MandatoryCommonModel }

let tr290v_reference : specification_reference =
  { spec_id = "TR290V";
    spec_title = "Intent Common Model - Vocabulary Reference";
    spec_version = "3.6.0";
    spec_role = MandatoryCommonModel }

let tr291h_reference : specification_reference =
  { spec_id = "TR291H";
    spec_title = "Intent Guarantee - Intent Extension Model";
    spec_version = "3.6.0";
    spec_role = ExtensionModel }

let tr292a_reference : specification_reference =
  { spec_id = "TR292A";
    spec_title = "Intent Management Elements";
    spec_version = "3.6.0";
    spec_role = BaseOntologyModel }

let tr292c_reference : specification_reference =
  { spec_id = "TR292C";
    spec_title = "Function Definition Ontology";
    spec_version = "3.6.0";
    spec_role = BaseOntologyModel }

let tr292d_reference : specification_reference =
  { spec_id = "TR292D";
    spec_title = "Quantity Ontology";
    spec_version = "3.6.0";
    spec_role = BaseOntologyModel }

let tr292e_reference : specification_reference =
  { spec_id = "TR292E";
    spec_title = "Conditions and Logical Operators";
    spec_version = "3.6.0";
    spec_role = BaseOntologyModel }

let tr292f_reference : specification_reference =
  { spec_id = "TR292F";
    spec_title = "Set Operators";
    spec_version = "3.6.0";
    spec_role = BaseOntologyModel }

let tr292g_reference : specification_reference =
  { spec_id = "TR292G";
    spec_title = "Metrics and Observations";
    spec_version = "3.6.0";
    spec_role = BaseOntologyModel }

let tr292i_reference : specification_reference =
  { spec_id = "TR292I";
    spec_title = "Security Ontology";
    spec_version = "3.8.0";
    spec_role = ExtensionModel }

let tr292r_reference : specification_reference =
  { spec_id = "TR292R";
    spec_title = "TM Forum Intent Ontology (TIO) - References";
    spec_version = "3.6.0";
    spec_role = SupportingReference }

let tr299_reference : specification_reference =
  { spec_id = "TR299";
    spec_title = "Intent Specification";
    spec_version = "3.6.0";
    spec_role = IntentSpecification }
