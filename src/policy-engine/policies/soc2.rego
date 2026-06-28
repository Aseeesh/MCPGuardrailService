package soc2

import rego.v1

default allow := false

violations contains msg if {
    contains(lower(input.content), "password")
    contains(lower(input.content), "plain")
    msg := "SOC2-CC6.1: Credentials must not be stored or transmitted in plaintext"
}

violations contains msg if {
    contains(lower(input.content), "log")
    contains(lower(input.content), "disable")
    msg := "SOC2-CC7.2: Audit logging must not be disabled"
}

violations contains msg if {
    contains(lower(input.content), "public")
    contains(lower(input.content), "access")
    not contains(lower(input.content), "restrict")
    msg := "SOC2-CC6.3: Public access without explicit access restrictions"
}

violations contains msg if {
    contains(lower(input.content), "backup")
    not contains(lower(input.content), "encrypt")
    msg := "SOC2-CC6.7: Backups must be encrypted"
}

allow if {
    count(violations) == 0
}
