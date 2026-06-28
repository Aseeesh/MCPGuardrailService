package pii.hipaa_test

import rego.v1

import data.pii.hipaa

test_phi_keyword_patient if {
	result := hipaa.violations with input as {
		"content": "The patient was admitted yesterday",
		"context": {"hipaa_authorized": false, "encryption": "none", "minimum_necessary": false},
	}
	count(result) > 0
	some v in result
	v.type == "phi_detected"
}

test_phi_authorized_allowed if {
	result := hipaa.violations with input as {
		"content": "The patient was admitted yesterday",
		"context": {"hipaa_authorized": true, "encryption": "AES-256", "minimum_necessary": true},
	}
	not any_phi_detected(result)
}

any_phi_detected(violations) if {
	some v in violations
	v.type == "phi_detected"
}

test_ssn_detected if {
	result := hipaa.violations with input as {
		"content": "SSN: 123-45-6789",
		"context": {"hipaa_authorized": false, "encryption": "none", "minimum_necessary": false},
	}
	count(result) > 0
	some v in result
	v.type == "ssn_detected"
}

test_mrn_detected if {
	result := hipaa.violations with input as {
		"content": "MRN: ABC123456 shows records",
		"context": {"hipaa_authorized": false, "encryption": "none", "minimum_necessary": false},
	}
	count(result) > 0
	some v in result
	v.type == "mrn_detected"
}

test_encryption_required if {
	result := hipaa.violations with input as {
		"content": "Patient diagnosis: hypertension",
		"context": {"hipaa_authorized": false, "encryption": "none", "minimum_necessary": false},
	}
	some v in result
	v.type == "encryption_required"
}

test_clean_content if {
	d := hipaa.decision with input as {
		"content": "The hospital has 200 beds and serves the community",
		"context": {"hipaa_authorized": false, "encryption": "AES-256", "minimum_necessary": true},
	}
	d == "allow"
}
