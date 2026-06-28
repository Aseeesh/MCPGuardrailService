package safety.content

import rego.v1

metadata := {
	"id": "SAF-001",
	"name": "Content Safety Detection",
	"version": "1.0.0",
	"frameworks": ["INTERNAL"],
	"controls": ["SAF-TOXIC", "SAF-UNSAFE", "SAF-HARMFUL"],
	"severity": "high",
}

default decision := "block"

toxic_patterns := [
	"kill", "murder", "assault", "attack",
	"hate speech", "slur", "discriminat",
]

unsafe_instruction_patterns := [
	"how to hack", "how to break into",
	"bypass security", "exploit vulnerability",
	"make a weapon", "build a bomb",
	"create malware", "write a virus",
]

self_harm_patterns := [
	"self-harm", "suicide method", "end my life",
]

violations contains violation if {
	some pattern in toxic_patterns
	contains(lower(input.content), pattern)
	not input.context.authorized_security_research
	violation := {
		"rule": "SAF-TOXIC-001",
		"type": "toxic_content",
		"severity": "high",
		"control": "SAF-TOXIC",
		"message": sprintf("Potentially toxic content detected: '%s'", [pattern]),
		"remediation": "block",
	}
}

violations contains violation if {
	some pattern in unsafe_instruction_patterns
	contains(lower(input.content), pattern)
	not input.context.authorized_security_research
	violation := {
		"rule": "SAF-UNSAFE-001",
		"type": "unsafe_instruction",
		"severity": "critical",
		"control": "SAF-UNSAFE",
		"message": "Unsafe instruction request detected",
		"remediation": "block",
	}
}

violations contains violation if {
	some pattern in self_harm_patterns
	contains(lower(input.content), pattern)
	violation := {
		"rule": "SAF-HARM-001",
		"type": "self_harm",
		"severity": "critical",
		"control": "SAF-HARMFUL",
		"message": "Self-harm related content detected — requires immediate escalation",
		"remediation": "escalate",
	}
}

decision := "allow" if {
	count(violations) == 0
}

decision := "escalate" if {
	some v in violations
	v.type == "self_harm"
}
