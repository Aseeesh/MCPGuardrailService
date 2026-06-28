package gdpr

import rego.v1

default allow := false

violations contains msg if {
    contains(lower(input.content), "email")
    not contains(lower(input.content), "consent")
    msg := "GDPR-ART6: Personal data (email) processed without documented consent basis"
}

violations contains msg if {
    contains(lower(input.content), "name")
    contains(lower(input.content), "address")
    msg := "GDPR-ART5: Multiple PII fields detected without data minimization"
}

violations contains msg if {
    contains(lower(input.content), "transfer")
    contains(lower(input.content), "outside")
    msg := "GDPR-ART46: Cross-border data transfer without adequate safeguards"
}

violations contains msg if {
    contains(lower(input.content), "retain")
    not contains(lower(input.content), "period")
    msg := "GDPR-ART5: Data retention mentioned without defined retention period"
}

allow if {
    count(violations) == 0
}
