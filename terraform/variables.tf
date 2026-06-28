variable "project_name" {
  description = "Project name prefix for all resources"
  type        = string
  default     = "guardrail"
}

variable "environment" {
  description = "Environment (dev, staging, prod)"
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod."
  }
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "eastus"
}

variable "db_password" {
  description = "PostgreSQL administrator password"
  type        = string
  sensitive   = true
}

variable "monthly_budget" {
  description = "Monthly budget in USD"
  type        = number
  default     = 50
}

variable "alert_emails" {
  description = "Emails for budget and monitoring alerts"
  type        = list(string)
  default     = []
}
