variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "project_name" { type = string }
variable "environment" { type = string }
variable "tags" { type = map(string) }

resource "azurerm_redis_cache" "main" {
  name                = "redis-${var.project_name}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  capacity            = 0
  family              = "C"
  sku_name            = var.environment == "prod" ? "Standard" : "Basic"
  minimum_tls_version = "1.2"
  enable_non_ssl_port = false
  tags                = var.tags

  redis_configuration {
    maxmemory_policy = "allkeys-lru"
  }
}

output "hostname" { value = azurerm_redis_cache.main.hostname }
output "port" { value = azurerm_redis_cache.main.ssl_port }
output "connection_string" {
  value     = "${azurerm_redis_cache.main.hostname}:${azurerm_redis_cache.main.ssl_port},password=${azurerm_redis_cache.main.primary_access_key},ssl=True,abortConnect=False"
  sensitive = true
}
