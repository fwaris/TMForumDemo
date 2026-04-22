Note: Keep this the abstract for the paper - don't change it.

## Abstract


Natural language (NL) intents will be increasingly the control mechanism of 5G/6G autonomous telecom networks. 

Example intent: "Provide an ultra-reliable low-latency 5G service for telemedicine and critical care operations at Mayo Clinic on an ongoing basis. Support up to 80 critical devices and 200 auxiliary endpoints. Maintain end-to-end latency below 10 ms ...".

This shift creates a safety and reliability problem: natural-language or weakly structured intents are attractive at the management plane, but they can be ambiguous, under-specified, and may not validate against additional provider-specific operational constraints. 

This paper presents a prototype, high-assurance, intent-admission shell for autonomous networks built around the TM Forum TMF921 Intent Management API. TM921 serves as 'front door' for NL intents and as such imposes the minimal semantic requirements via the TR292 OWL ontology. However any specific telecom provider implementation of TMF921 will have additional requirements that are not covered by TR292.

This paper shows how NL intents may be trusted -- with almost provably-correct levels of assurance -- using formal verification systems rooted in dependently-typed languages. First (offline) the TR292 ontology is converted to the equivalent dependent types of the F* language. These serve as propositions against which incoming intents may be checked. Then (online) received natural-language intents are translated by a Large Language Model (LLM) to F* code that is validated (type-checked) against the TR292 dependent types, by the F* proof checker. Only validated intents are admitted.

Further, since TMF921 structure-imposition is necessarily generic, we demo provider-specific intent validation as further refinements of the TR292 types. 

It is observed that valid intents are translated and validated with high reliability. Specifically, the LLM (GPT-5.4) produces correct F* code in approximately 99% of cases on the first attempt, and achieves 100% correctness with a single retry. These results suggest that the apparent non-determinism of natural language and LLM-generated outputs can be effectively mitigated through dependent-type–based validation.
