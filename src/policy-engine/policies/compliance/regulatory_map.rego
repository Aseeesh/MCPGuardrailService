package compliance.regulatory

import rego.v1

metadata := {
	"id": "CMP-REG-001",
	"name": "Regulatory Control Mapping",
	"version": "1.0.0",
	"frameworks": ["GDPR", "HIPAA", "SOC2"],
	"description": "Maps content violations to specific regulatory controls and generates compliance evidence",
}

control_map := {
	"GDPR": {
		"ART5": {"name": "Principles", "controls": ["data_minimization", "purpose_limitation", "storage_limitation", "accuracy", "integrity"]},
		"ART6": {"name": "Lawful Basis", "controls": ["consent", "contract", "legal_obligation", "vital_interest", "public_interest", "legitimate_interest"]},
		"ART9": {"name": "Special Categories", "controls": ["explicit_consent", "employment", "vital_interest", "public_health"]},
		"ART17": {"name": "Right to Erasure", "controls": ["deletion_request", "retention_expiry", "consent_withdrawal"]},
		"ART25": {"name": "Data Protection by Design", "controls": ["pseudonymization", "encryption", "access_controls"]},
		"ART33": {"name": "Breach Notification", "controls": ["72h_notification", "documentation", "dpa_communication"]},
		"ART35": {"name": "DPIA", "controls": ["risk_assessment", "necessity_test", "safeguards"]},
		"ART46": {"name": "Cross-Border Transfer", "controls": ["adequacy_decision", "standard_clauses", "binding_rules"]},
	},
	"HIPAA": {
		"164.308": {"name": "Administrative Safeguards", "controls": ["risk_analysis", "workforce_training", "contingency_plan", "evaluation"]},
		"164.310": {"name": "Physical Safeguards", "controls": ["facility_access", "workstation_use", "device_controls"]},
		"164.312": {"name": "Technical Safeguards", "controls": ["access_control", "audit_controls", "integrity", "transmission_security"]},
		"164.502": {"name": "Uses and Disclosures", "controls": ["minimum_necessary", "authorization", "treatment_ops_payment"]},
		"164.514": {"name": "De-identification", "controls": ["safe_harbor", "expert_determination", "limited_data_set"]},
		"164.524": {"name": "Access Rights", "controls": ["patient_access", "timely_response", "electronic_copy"]},
	},
	"SOC2": {
		"CC6.1": {"name": "Logical Access", "controls": ["authentication", "authorization", "credential_management"]},
		"CC6.3": {"name": "Role-Based Access", "controls": ["rbac", "least_privilege", "segregation_of_duties"]},
		"CC6.5": {"name": "System Boundary", "controls": ["network_segmentation", "firewall", "dmz"]},
		"CC6.7": {"name": "Data Transmission", "controls": ["encryption_transit", "encryption_rest", "key_management"]},
		"CC7.2": {"name": "Monitoring", "controls": ["log_collection", "anomaly_detection", "incident_response"]},
		"CC8.1": {"name": "Change Management", "controls": ["change_approval", "testing", "rollback"]},
	},
}

coverage_report[framework] := report if {
	some framework, sections in control_map
	report := {section_id: {
		"name": section.name,
		"total_controls": count(section.controls),
		"mapped": true,
	} |
		some section_id, section in sections
	}
}

applicable_controls contains control if {
	some framework in input.frameworks
	some section_id, section in control_map[framework]
	some ctrl in section.controls
	control := {
		"framework": framework,
		"section": section_id,
		"section_name": section.name,
		"control": ctrl,
	}
}
