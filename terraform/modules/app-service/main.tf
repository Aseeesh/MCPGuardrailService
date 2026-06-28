variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "project_name" { type = string }
variable "environment" { type = string }
variable "app_name" { type = string }
variable "runtime_stack" { type = string }
variable "runtime_version" { type = string }
variable "sku_name" { type = string, default = "F1" }
variable "is_static" { type = bool, default = false }
variable "app_settings" { type = map(string), default = {} }
variable "tags" { type = map(string) }

resource "azurerm_service_plan" "main" {
  count               = var.is_static ? 0 : 1
  name                = "asp-${var.project_name}-${var.app_name}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  os_type             = "Linux"
  sku_name            = var.sku_name
  tags                = var.tags
}

resource "azurerm_linux_web_app" "main" {
  count               = var.is_static ? 0 : 1
  name                = "app-${var.project_name}-${var.app_name}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  service_plan_id     = azurerm_service_plan.main[0].id
  https_only          = true
  tags                = var.tags

  site_config {
    always_on         = var.sku_name != "F1"
    ftps_state        = "Disabled"
    minimum_tls_version = "1.2"
    health_check_path = "/health"

    dynamic "application_stack" {
      for_each = var.runtime_stack == "dotnet" ? [1] : []
      content {
        dotnet_version = var.runtime_version
      }
    }

    dynamic "application_stack" {
      for_each = var.runtime_stack == "python" ? [1] : []
      content {
        python_version = var.runtime_version
      }
    }

    dynamic "application_stack" {
      for_each = var.runtime_stack == "node" ? [1] : []
      content {
        node_version = var.runtime_version
      }
    }
  }

  app_settings = merge(var.app_settings, {
    "WEBSITE_RUN_FROM_PACKAGE" = "1"
    "SCM_DO_BUILD_DURING_DEPLOYMENT" = "true"
  })

  identity {
    type = "SystemAssigned"
  }
}

resource "azurerm_static_web_app" "main" {
  count               = var.is_static ? 1 : 0
  name                = "swa-${var.project_name}-${var.app_name}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku_tier            = "Free"
  sku_size            = "Free"
  tags                = var.tags
}

output "default_hostname" {
  value = var.is_static ? (
    length(azurerm_static_web_app.main) > 0 ? azurerm_static_web_app.main[0].default_host_name : ""
  ) : (
    length(azurerm_linux_web_app.main) > 0 ? azurerm_linux_web_app.main[0].default_hostname : ""
  )
}

output "principal_id" {
  value = var.is_static ? "" : (
    length(azurerm_linux_web_app.main) > 0 ? azurerm_linux_web_app.main[0].identity[0].principal_id : ""
  )
}
