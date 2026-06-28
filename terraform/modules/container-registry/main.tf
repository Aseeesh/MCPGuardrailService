variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "project_name" { type = string }
variable "environment" { type = string }
variable "tags" { type = map(string) }

resource "azurerm_container_registry" "main" {
  name                = "acr${var.project_name}${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = "Basic"
  admin_enabled       = true
  tags                = var.tags

  retention_policy_in_days = 7
}

output "login_server" { value = azurerm_container_registry.main.login_server }
output "admin_username" { value = azurerm_container_registry.main.admin_username }
output "admin_password" {
  value     = azurerm_container_registry.main.admin_password
  sensitive = true
}
