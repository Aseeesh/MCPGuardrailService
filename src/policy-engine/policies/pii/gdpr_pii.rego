package pii.gdpr

import rego.v1

metadata := {
	"id": "PII-GDPR-001",
	"name": "GDPR PII Detection",
	"version": "1.2.0",
	"frameworks": ["GDPR"],
	"controls": ["GDPR-ART5", "GDPR-ART6", "GDPR-ART9", "GDPR-ART17"],
	"severity": "high",
	"description": "Detects personal data under GDPR and enforces data protection principles",
}

default allow := false

default decision := "block"

pii_patterns := {
	"email": `[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}`,
	"phone_eu": `\+?[0-9]{1,4}[\s.-]?\(?[0-9]{1,4}\)?[\s.-]?[0-9]{3,10}`,
	"iban": `[A-Z]{2}[0-9]{2}[A-Z0-9]{4}[0-9]{7}([A-Z0-9]?){0,16}`,
	"passport_eu": `[A-Z]{1,2}[0-9]{6,9}`,
	"national_id": `[0-9]{2,3}[-.\s]?[0-9]{2,4}[-.\s]?[0-9]{2,6}`,
}

special_category_keywords := [
	"racial", "ethnic", "political", "religious",
	"trade union", "genetic", "biometric", "health",
	"sex life", "sexual orientation",
]

violations contains violation if {
	some pattern_name, _pattern in pii_patterns
	contains_pii(input.content, pattern_name)
	violation := {
		"rule": "PII-GDPR-001",
		"type": "pii_detected",
		"pattern": pattern_name,
		"severity": "high",
		"control": "GDPR-ART5",
		"message": sprintf("Personal data pattern '%s' detected without lawful basis", [pattern_name]),
		"remediation": "redact",
	}
}

violations contains violation if {
	some keyword in special_category_keywords
	contains(lower(input.content), keyword)
	violation := {
		"rule": "PII-GDPR-002",
		"type": "special_category",
		"pattern": keyword,
		"severity": "critical",
		"control": "GDPR-ART9",
		"message": sprintf("Special category data detected: '%s'. Requires explicit consent under Art. 9", [keyword]),
		"remediation": "block",
	}
}

violations contains violation if {
	input.context.purpose != ""
	not valid_purpose(input.context.purpose)
	violation := {
		"rule": "PII-GDPR-003",
		"type": "purpose_limitation",
		"severity": "high",
		"control": "GDPR-ART5",
		"message": "Processing purpose not in approved purposes list",
		"remediation": "escalate",
	}
}

violations contains violation if {
	input.context.retention_days > 0
	input.context.retention_days > max_retention(input.context.data_category)
	violation := {
		"rule": "PII-GDPR-004",
		"type": "storage_limitation",
		"severity": "medium",
		"control": "GDPR-ART5",
		"message": sprintf("Retention period %d days exceeds maximum for category '%s'", [input.context.retention_days, input.context.data_category]),
		"remediation": "escalate",
	}
}

violations contains violation if {
	input.context.cross_border == true
	not input.context.adequacy_decision
	not input.context.standard_clauses
	violation := {
		"rule": "PII-GDPR-005",
		"type": "cross_border_transfer",
		"severity": "critical",
		"control": "GDPR-ART46",
		"message": "Cross-border transfer without adequacy decision or standard contractual clauses",
		"remediation": "block",
	}
}

decision := "allow" if {
	count(violations) == 0
}

decision := "redact" if {
	count(violations) > 0
	every v in violations {
		v.severity != "critical"
	}
}

contains_pii(content, pattern_name) if {
	pattern_name == "email"
	regex.match(`[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}`, content)
}

contains_pii(content, pattern_name) if {
	pattern_name == "iban"
	regex.match(`[A-Z]{2}[0-9]{2}[A-Z0-9]{4}[0-9]{7}`, content)
}

contains_pii(content, pattern_name) if {
	pattern_name == "phone_eu"
	regex.match(`\+[0-9]{1,4}[\s-]?[0-9]{4,14}`, content)
}

contains_pii(content, pattern_name) if {
	pattern_name == "passport_eu"
	regex.match(`[A-Z]{1,2}[0-9]{6,9}`, content)
}

contains_pii(content, pattern_name) if {
	pattern_name == "national_id"
	regex.match(`[0-9]{3}[-]?[0-9]{2}[-]?[0-9]{4}`, content)
}

valid_purpose(purpose) if {
	approved := {"service_delivery", "legal_obligation", "vital_interest", "public_interest", "legitimate_interest", "consent"}
	approved[purpose]
}

max_retention(category) := 365 if {
	category == "contact"
}

max_retention(category) := 90 if {
	category == "behavioral"
}

max_retention(category) := 730 if {
	category == "transactional"
}

max_retention(category) := 2555 if {
	category == "legal"
}

max_retention(category) := 180 if {
	not category in {"contact", "behavioral", "transactional", "legal"}
}
