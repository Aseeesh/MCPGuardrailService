package brand.tone

import rego.v1

metadata := {
	"id": "BRD-001",
	"name": "Brand Tone & Voice Policy",
	"version": "1.0.0",
	"frameworks": ["INTERNAL"],
	"controls": ["BRD-TONE", "BRD-LANG"],
	"severity": "low",
}

default decision := "allow"

prohibited_language := [
	"sucks", "stupid", "dumb", "idiot",
	"crap", "clueless",
]

competitor_mentions := [
	"competitor_a", "competitor_b", "competitor_c",
]

required_disclaimers := {
	"financial": "This is not financial advice.",
	"medical": "Consult a healthcare professional.",
	"legal": "This is not legal advice.",
}

violations contains violation if {
	some word in prohibited_language
	contains(lower(input.content), word)
	violation := {
		"rule": "BRD-TONE-001",
		"type": "prohibited_language",
		"severity": "low",
		"control": "BRD-TONE",
		"message": sprintf("Prohibited brand language detected: '%s'", [word]),
		"remediation": "rewrite",
	}
}

violations contains violation if {
	some competitor in competitor_mentions
	contains(lower(input.content), competitor)
	not input.context.comparison_approved
	violation := {
		"rule": "BRD-TONE-002",
		"type": "competitor_mention",
		"severity": "medium",
		"control": "BRD-TONE",
		"message": "Unapproved competitor mention detected",
		"remediation": "rewrite",
	}
}

violations contains violation if {
	some domain, disclaimer in required_disclaimers
	input.context.content_domain == domain
	not contains(input.content, disclaimer)
	violation := {
		"rule": "BRD-LANG-001",
		"type": "missing_disclaimer",
		"severity": "medium",
		"control": "BRD-LANG",
		"message": sprintf("Missing required disclaimer for %s content", [domain]),
		"remediation": "rewrite",
	}
}

decision := "rewrite" if {
	count(violations) > 0
}
