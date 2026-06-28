package hipaa

import rego.v1

default allow := false

violations contains msg if {
    contains(lower(input.content), "patient")
    not contains(lower(input.content), "encrypted")
    msg := "HIPAA-164.312: PHI detected without encryption requirement"
}

violations contains msg if {
    contains(lower(input.content), "diagnosis")
    msg := "HIPAA-164.502: Clinical diagnosis information requires minimum necessary standard"
}

violations contains msg if {
    contains(lower(input.content), "ssn")
    msg := "HIPAA-164.514: SSN is a direct identifier requiring de-identification"
}

violations contains msg if {
    contains(lower(input.content), "medical")
    contains(lower(input.content), "record")
    not contains(lower(input.content), "authorized")
    msg := "HIPAA-164.508: Medical records access requires valid authorization"
}

allow if {
    count(violations) == 0
}
