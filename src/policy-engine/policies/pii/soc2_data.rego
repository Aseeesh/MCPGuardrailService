package pii.soc2

import rego.v1

metadata := {
	"id": "PII-SOC2-001",
	"name": "SOC2 Data Protection",
	"version": "1.0.0",
	"frameworks": ["SOC2"],
	"controls": ["CC6.1", "CC6.5", "CC6.7", "CC7.2", "CC8.1"],
	"severity": "high",
}

default decision := "block"

violations contains violation if {
	regex.match(`(?i)(password|passwd|secret|api.?key|token)\s*[:=]\s*\S+`, input.content)
	violation := {
		"rule": "SOC2-CC6.1-001",
		"type": "credential_exposure",
		"severity": "critical",
		"control": "CC6.1",
		"message": "Credential or secret detected in content",
		"remediation": "redact",
	}
}

violations contains violation if {
	regex.match(`(?i)(aws_access_key|aws_secret|AKIA[0-9A-Z]{16})`, input.content)
	violation := {
		"rule": "SOC2-CC6.1-002",
		"type": "cloud_credential",
		"severity": "critical",
		"control": "CC6.1",
		"message": "Cloud provider credential detected",
		"remediation": "block",
	}
}

violations contains violation if {
	contains(lower(input.content), "public access")
	not contains(lower(input.content), "denied")
	not contains(lower(input.content), "disabled")
	violation := {
		"rule": "SOC2-CC6.5-001",
		"type": "public_access",
		"severity": "high",
		"control": "CC6.5",
		"message": "Public access configuration without explicit denial",
		"remediation": "escalate",
	}
}

violations contains violation if {
	contains(lower(input.content), "backup")
	not contains(lower(input.content), "encrypt")
	violation := {
		"rule": "SOC2-CC6.7-001",
		"type": "unencrypted_backup",
		"severity": "high",
		"control": "CC6.7",
		"message": "Backup configuration without encryption",
		"remediation": "escalate",
	}
}

violations contains violation if {
	contains(lower(input.content), "logging")
	contains(lower(input.content), "disabled")
	violation := {
		"rule": "SOC2-CC7.2-001",
		"type": "logging_disabled",
		"severity": "critical",
		"control": "CC7.2",
		"message": "Audit logging disabled — violates monitoring controls",
		"remediation": "block",
	}
}

decision := "allow" if {
	count(violations) == 0
}
