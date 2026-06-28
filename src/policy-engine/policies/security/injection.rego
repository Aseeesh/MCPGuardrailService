package security.injection

import rego.v1

metadata := {
	"id": "SEC-INJ-001",
	"name": "Injection Attack Detection",
	"version": "1.3.0",
	"frameworks": ["OWASP-A03", "SOC2"],
	"controls": ["CC6.1", "CC6.8"],
	"severity": "critical",
}

default decision := "block"

sql_patterns := [
	`(?i)(union\s+(all\s+)?select)`,
	`(?i)(;\s*(drop|delete|update|insert|alter)\s)`,
	`(?i)(or\s+1\s*=\s*1)`,
	`(?i)(and\s+1\s*=\s*1)`,
	`(?i)('\s*(or|and)\s+'[^']*'\s*=\s*'[^']*)`,
	`(?i)(exec(\s+|\()sp_)`,
	`(?i)(xp_cmdshell)`,
	`(?i)(waitfor\s+delay)`,
	`(?i)(benchmark\s*\()`,
	`(?i)(load_file\s*\()`,
	`(?i)(into\s+(out|dump)file)`,
]

xss_patterns := [
	`<script[^>]*>`,
	`javascript\s*:`,
	`on(error|load|click|mouseover|focus|blur)\s*=`,
	`<iframe[^>]*>`,
	`<object[^>]*>`,
	`<embed[^>]*>`,
	`<svg[^>]*on\w+\s*=`,
	`expression\s*\(`,
	`url\s*\(\s*['"]?\s*javascript`,
	`data\s*:\s*text\/html`,
]

command_injection_patterns := [
	`[;&|]\s*(cat|ls|pwd|whoami|id|uname|curl|wget)\b`,
	`\$\(.*\)`,
	"`[^`]*`",
	`\|\|\s*(rm|del|format|mkfs)`,
	`;\s*(rm|del)\s+-rf?\s`,
	`\.\./\.\./`,
]

violations contains violation if {
	some pattern in sql_patterns
	regex.match(pattern, input.content)
	violation := {
		"rule": "SEC-INJ-001",
		"type": "sql_injection",
		"severity": "critical",
		"control": "CC6.1",
		"message": "SQL injection pattern detected",
		"remediation": "block",
	}
}

violations contains violation if {
	some pattern in xss_patterns
	regex.match(pattern, input.content)
	violation := {
		"rule": "SEC-INJ-002",
		"type": "xss",
		"severity": "critical",
		"control": "CC6.1",
		"message": "Cross-site scripting (XSS) pattern detected",
		"remediation": "redact",
	}
}

violations contains violation if {
	some pattern in command_injection_patterns
	regex.match(pattern, input.content)
	violation := {
		"rule": "SEC-INJ-003",
		"type": "command_injection",
		"severity": "critical",
		"control": "CC6.8",
		"message": "Command injection pattern detected",
		"remediation": "block",
	}
}

violations contains violation if {
	regex.match(`(?i)({{.*}}|{%.*%}|\$\{.*\})`, input.content)
	input.context.source == "user_input"
	violation := {
		"rule": "SEC-INJ-004",
		"type": "template_injection",
		"severity": "high",
		"control": "CC6.1",
		"message": "Server-side template injection pattern detected in user input",
		"remediation": "block",
	}
}

decision := "allow" if {
	count(violations) == 0
}
