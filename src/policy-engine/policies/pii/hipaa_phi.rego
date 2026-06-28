package pii.hipaa

import rego.v1

metadata := {
	"id": "PII-HIPAA-001",
	"name": "HIPAA PHI Detection",
	"version": "1.1.0",
	"frameworks": ["HIPAA"],
	"controls": ["HIPAA-164.502", "HIPAA-164.514", "HIPAA-164.312"],
	"severity": "critical",
	"description": "Detects Protected Health Information under HIPAA Safe Harbor",
}

default decision := "block"

hipaa_identifiers := [
	"name", "address", "dates", "phone", "fax", "email",
	"ssn", "medical_record", "health_plan", "account",
	"certificate", "vehicle", "device", "url", "ip",
	"biometric", "photo", "any_unique",
]

phi_keywords := [
	"patient", "diagnosis", "treatment", "prescription",
	"medical record", "health plan", "healthcare",
	"clinical", "hospital", "physician", "lab result",
	"blood type", "allergy", "medication", "surgery",
	"prognosis", "symptom", "vital sign",
]

violations contains violation if {
	some keyword in phi_keywords
	contains(lower(input.content), keyword)
	not input.context.hipaa_authorized
	violation := {
		"rule": "PHI-HIPAA-001",
		"type": "phi_detected",
		"keyword": keyword,
		"severity": "critical",
		"control": "HIPAA-164.502",
		"message": sprintf("PHI keyword '%s' detected without valid HIPAA authorization", [keyword]),
		"remediation": "block",
	}
}

violations contains violation if {
	regex.match(`[0-9]{3}-[0-9]{2}-[0-9]{4}`, input.content)
	violation := {
		"rule": "PHI-HIPAA-002",
		"type": "ssn_detected",
		"severity": "critical",
		"control": "HIPAA-164.514",
		"message": "SSN pattern detected — direct identifier requiring de-identification",
		"remediation": "redact",
	}
}

violations contains violation if {
	regex.match(`MRN[-:\s]?[A-Z0-9]{6,12}`, upper(input.content))
	violation := {
		"rule": "PHI-HIPAA-003",
		"type": "mrn_detected",
		"severity": "critical",
		"control": "HIPAA-164.514",
		"message": "Medical Record Number detected — requires de-identification",
		"remediation": "redact",
	}
}

violations contains violation if {
	input.context.encryption != "AES-256"
	input.context.encryption != "AES-128"
	some keyword in phi_keywords
	contains(lower(input.content), keyword)
	violation := {
		"rule": "PHI-HIPAA-004",
		"type": "encryption_required",
		"severity": "high",
		"control": "HIPAA-164.312",
		"message": "PHI must be encrypted with AES-128 or AES-256 at rest and in transit",
		"remediation": "escalate",
	}
}

violations contains violation if {
	not input.context.minimum_necessary
	some keyword in phi_keywords
	contains(lower(input.content), keyword)
	violation := {
		"rule": "PHI-HIPAA-005",
		"type": "minimum_necessary",
		"severity": "high",
		"control": "HIPAA-164.502",
		"message": "Minimum necessary standard not applied to PHI disclosure",
		"remediation": "rewrite",
	}
}

decision := "allow" if {
	count(violations) == 0
}

decision := "redact" if {
	count(violations) > 0
	every v in violations {
		v.remediation == "redact"
	}
}
