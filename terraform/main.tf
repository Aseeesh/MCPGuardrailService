terraform {
  required_version = ">= 1.5.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.85"
    }
  }

  backend "azurerm" {
    resource_group_name  = "rg-guardrail-tfstate"
    storage_account_name = "stguardrailtfstate"
    container_name       = "tfstate"
    key                  = "guardrail.tfstate"
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy = false
    }
  }
}

data "azurerm_client_config" "current" {}

# --- Resource Group ---
resource "azurerm_resource_group" "main" {
  name     = "rg-${var.project_name}-${var.environment}"
  location = var.location
  tags     = local.tags
}

locals {
  tags = {
    project     = var.project_name
    environment = var.environment
    managed_by  = "terraform"
  }
}

# --- Modules ---

module "networking" {
  source              = "./modules/networking"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  tags                = local.tags
}

module "database" {
  source              = "./modules/database"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  db_password         = var.db_password
  subnet_id           = module.networking.database_subnet_id
  tags                = local.tags
}

module "redis" {
  source              = "./modules/redis"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  tags                = local.tags
}

module "storage" {
  source              = "./modules/storage"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  tags                = local.tags
}

module "container_registry" {
  source              = "./modules/container-registry"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  tags                = local.tags
}

module "api_gateway" {
  source              = "./modules/app-service"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  app_name            = "api"
  runtime_stack       = "dotnet"
  runtime_version     = "9.0"
  sku_name            = var.environment == "prod" ? "B1" : "F1"
  tags                = local.tags

  app_settings = {
    "ConnectionStrings__Postgres"  = module.database.connection_string
    "Redis__ConnectionString"      = module.redis.connection_string
    "AiService__BaseUrl"           = "https://${module.ai_service.default_hostname}"
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = module.monitoring.connection_string
  }
}

module "ai_service" {
  source              = "./modules/app-service"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  app_name            = "ai"
  runtime_stack       = "python"
  runtime_version     = "3.12"
  sku_name            = var.environment == "prod" ? "B1" : "F1"
  tags                = local.tags

  app_settings = {
    "OLLAMA_BASE_URL" = "http://localhost:11434"
    "OPA_URL"         = "http://localhost:8181"
    "REDIS_URL"       = "redis://${module.redis.hostname}:${module.redis.port}"
    "POSTGRES_URL"    = module.database.connection_string
  }
}

module "frontend" {
  source              = "./modules/app-service"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  app_name            = "web"
  runtime_stack       = "node"
  runtime_version     = "22-lts"
  sku_name            = "Free"
  is_static           = true
  tags                = local.tags

  app_settings = {
    "VITE_API_URL" = "https://${module.api_gateway.default_hostname}"
  }
}

module "monitoring" {
  source              = "./modules/monitoring"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project_name        = var.project_name
  environment         = var.environment
  tags                = local.tags
}

# --- Budget Alert ---
resource "azurerm_consumption_budget_resource_group" "main" {
  name              = "budget-${var.project_name}-${var.environment}"
  resource_group_id = azurerm_resource_group.main.id
  amount            = var.monthly_budget
  time_grain        = "Monthly"

  time_period {
    start_date = formatdate("YYYY-MM-01'T'00:00:00Z", timestamp())
  }

  notification {
    enabled        = true
    threshold      = 80
    operator       = "GreaterThan"
    contact_emails = var.alert_emails
  }

  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThan"
    contact_emails = var.alert_emails
  }

  lifecycle {
    ignore_changes = [time_period]
  }
}
