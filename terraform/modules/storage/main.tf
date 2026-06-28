variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "project_name" { type = string }
variable "environment" { type = string }
variable "tags" { type = map(string) }

resource "azurerm_storage_account" "main" {
  name                     = "st${var.project_name}${var.environment}"
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = var.environment == "prod" ? "GRS" : "LRS"
  min_tls_version          = "TLS1_2"
  tags                     = var.tags

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 30
    }

    container_delete_retention_policy {
      days = 7
    }
  }
}

resource "azurerm_storage_container" "audit_payloads" {
  name                  = "audit-payloads"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

resource "azurerm_storage_container" "audit_archive" {
  name                  = "audit-archive"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

resource "azurerm_storage_container" "compliance_reports" {
  name                  = "compliance-reports"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

# Lifecycle: move audit payloads to cool after 30 days, archive after 90
resource "azurerm_storage_management_policy" "lifecycle" {
  storage_account_id = azurerm_storage_account.main.id

  rule {
    name    = "audit-lifecycle"
    enabled = true

    filters {
      prefix_match = ["audit-payloads/"]
      blob_types   = ["blockBlob"]
    }

    actions {
      base_blob {
        tier_to_cool_after_days_since_modification_greater_than    = 30
        tier_to_archive_after_days_since_modification_greater_than = 90
        delete_after_days_since_modification_greater_than          = 365
      }
    }
  }

  rule {
    name    = "archive-retention"
    enabled = true

    filters {
      prefix_match = ["audit-archive/"]
      blob_types   = ["blockBlob"]
    }

    actions {
      base_blob {
        tier_to_archive_after_days_since_modification_greater_than = 7
        delete_after_days_since_modification_greater_than          = 2555
      }
    }
  }
}

# Immutability policy for audit payloads (write-once)
resource "azurerm_storage_blob_inventory_policy" "audit" {
  storage_account_id = azurerm_storage_account.main.id

  rules {
    name                   = "audit-inventory"
    storage_container_name = azurerm_storage_container.audit_payloads.name
    format                 = "Csv"
    schedule               = "Weekly"
    scope                  = "Container"

    schema_fields = [
      "Name",
      "Creation-Time",
      "Last-Modified",
      "Content-Length",
      "Content-MD5",
      "BlobType",
      "AccessTier",
    ]
  }
}

output "account_name" { value = azurerm_storage_account.main.name }
output "primary_endpoint" { value = azurerm_storage_account.main.primary_blob_endpoint }
output "primary_key" {
  value     = azurerm_storage_account.main.primary_access_key
  sensitive = true
}
