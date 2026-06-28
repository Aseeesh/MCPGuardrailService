output "resource_group" {
  value = azurerm_resource_group.main.name
}

output "api_url" {
  value = "https://${module.api_gateway.default_hostname}"
}

output "ai_service_url" {
  value = "https://${module.ai_service.default_hostname}"
}

output "frontend_url" {
  value = "https://${module.frontend.default_hostname}"
}

output "postgres_host" {
  value     = module.database.hostname
  sensitive = true
}

output "redis_host" {
  value = module.redis.hostname
}

output "storage_account" {
  value = module.storage.account_name
}

output "container_registry" {
  value = module.container_registry.login_server
}

output "app_insights_key" {
  value     = module.monitoring.instrumentation_key
  sensitive = true
}
