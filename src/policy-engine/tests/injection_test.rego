package security.injection_test

import rego.v1

import data.security.injection

test_sql_union_select if {
	result := injection.violations with input as {
		"content": "1 UNION SELECT username, password FROM users",
		"context": {"source": "user_input"},
	}
	count(result) > 0
	some v in result
	v.type == "sql_injection"
}

test_sql_drop_table if {
	result := injection.violations with input as {
		"content": "'; DROP TABLE users; --",
		"context": {"source": "user_input"},
	}
	count(result) > 0
	some v in result
	v.type == "sql_injection"
}

test_sql_or_1_equals_1 if {
	result := injection.violations with input as {
		"content": "admin' or 1=1 --",
		"context": {"source": "user_input"},
	}
	count(result) > 0
}

test_xss_script_tag if {
	result := injection.violations with input as {
		"content": "<script>alert('xss')</script>",
		"context": {"source": "user_input"},
	}
	count(result) > 0
	some v in result
	v.type == "xss"
}

test_xss_event_handler if {
	result := injection.violations with input as {
		"content": "<img onerror=alert(1) src=x>",
		"context": {"source": "user_input"},
	}
	count(result) > 0
	some v in result
	v.type == "xss"
}

test_command_injection_cat if {
	result := injection.violations with input as {
		"content": "; cat /etc/passwd",
		"context": {"source": "user_input"},
	}
	count(result) > 0
	some v in result
	v.type == "command_injection"
}

test_template_injection if {
	result := injection.violations with input as {
		"content": "Hello {{7*7}}",
		"context": {"source": "user_input"},
	}
	count(result) > 0
	some v in result
	v.type == "template_injection"
}

test_clean_sql_allowed if {
	result := injection.violations with input as {
		"content": "SELECT name FROM users WHERE id = 1",
		"context": {"source": "internal"},
	}
	not any_sql_injection(result)
}

any_sql_injection(violations) if {
	some v in violations
	v.type == "sql_injection"
}

test_clean_content_allowed if {
	d := injection.decision with input as {
		"content": "Hello, how are you today?",
		"context": {"source": "user_input"},
	}
	d == "allow"
}
