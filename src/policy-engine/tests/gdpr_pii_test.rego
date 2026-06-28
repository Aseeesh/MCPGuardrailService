package pii.gdpr_test

import rego.v1

import data.pii.gdpr

test_email_detected if {
	result := gdpr.violations with input as {
		"content": "Contact user@example.com for details",
		"context": {"purpose": "", "retention_days": 0, "cross_border": false, "data_category": ""},
	}
	count(result) > 0
	some v in result
	v.type == "pii_detected"
	v.pattern == "email"
}

test_iban_detected if {
	result := gdpr.violations with input as {
		"content": "Transfer to DE89370400440532013000",
		"context": {"purpose": "", "retention_days": 0, "cross_border": false, "data_category": ""},
	}
	count(result) > 0
	some v in result
	v.pattern == "iban"
}

test_special_category_health if {
	result := gdpr.violations with input as {
		"content": "The health records show genetic predisposition",
		"context": {"purpose": "", "retention_days": 0, "cross_border": false, "data_category": ""},
	}
	count(result) > 0
	some v in result
	v.type == "special_category"
}

test_invalid_purpose if {
	result := gdpr.violations with input as {
		"content": "Process user data",
		"context": {"purpose": "marketing_spam", "retention_days": 0, "cross_border": false, "data_category": ""},
	}
	count(result) > 0
	some v in result
	v.type == "purpose_limitation"
}

test_valid_purpose_consent if {
	result := gdpr.violations with input as {
		"content": "No PII here, just a normal message",
		"context": {"purpose": "consent", "retention_days": 0, "cross_border": false, "data_category": ""},
	}
	every v in result {
		v.type != "purpose_limitation"
	}
}

test_retention_exceeded if {
	result := gdpr.violations with input as {
		"content": "Store contact info",
		"context": {"purpose": "", "retention_days": 500, "cross_border": false, "data_category": "contact"},
	}
	count(result) > 0
	some v in result
	v.type == "storage_limitation"
}

test_retention_within_limit if {
	result := gdpr.violations with input as {
		"content": "Normal text without PII",
		"context": {"purpose": "", "retention_days": 100, "cross_border": false, "data_category": "contact"},
	}
	every v in result {
		v.type != "storage_limitation"
	}
}

test_cross_border_no_safeguards if {
	result := gdpr.violations with input as {
		"content": "Transfer data",
		"context": {"purpose": "", "retention_days": 0, "cross_border": true, "adequacy_decision": false, "standard_clauses": false, "data_category": ""},
	}
	count(result) > 0
	some v in result
	v.type == "cross_border_transfer"
}

test_clean_content_allowed if {
	d := gdpr.decision with input as {
		"content": "The weather is nice today",
		"context": {"purpose": "", "retention_days": 0, "cross_border": false, "data_category": ""},
	}
	d == "allow"
}
