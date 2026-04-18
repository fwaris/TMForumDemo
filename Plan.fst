module Plan

type category  = | All | Military_veteran 

type plan = {
  title : string;
  category : category
}
