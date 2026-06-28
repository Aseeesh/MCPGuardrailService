#!/bin/bash
set -e

# Azure Security Configuration for Guardrail Service
# Run after initial Terraform deployment

ENVIRONMENT="${1:-dev}"
RG="rg-guardrail-$ENVIRONMENT"
PROJECT="guardrail"

echo "=== Security Configuration: $ENVIRONMENT ==="

# 1. Enable managed identities for App Services
echo "--- Managed Identities ---"
for APP in api ai; do
    az webapp identity assign \
        --name "app-${PROJECT}-${APP}-${ENVIRONMENT}" \
        --resource-group "$RG" \
        2>/dev/null && echo "  ✓ app-${PROJECT}-${APP}-${ENVIRONMENT}" || echo "  ⊘ already assigned"
done

# 2. Configure Key Vault (if exists)
KV_NAME="kv-${PROJECT}-${ENVIRONMENT}"
echo ""
echo "--- Key Vault ---"
if az keyvault show --name "$KV_NAME" --resource-group "$RG" &>/dev/null; then
    echo "  Key Vault exists: $KV_NAME"

    # Store secrets
    az keyvault secret set --vault-name "$KV_NAME" --name "db-password" --value "${DB_PASSWORD}" --only-show-errors >/dev/null
    echo "  ✓ db-password stored"
else
    echo "  Creating Key Vault..."
    az keyvault create \
        --name "$KV_NAME" \
        --resource-group "$RG" \
        --location eastus \
        --sku standard \
        --enable-soft-delete true \
        --retention-days 7
    echo "  ✓ Key Vault created"
fi

# 3. Network security
echo ""
echo "--- Network Security ---"

# Restrict App Service to HTTPS only
for APP in api ai; do
    az webapp update \
        --name "app-${PROJECT}-${APP}-${ENVIRONMENT}" \
        --resource-group "$RG" \
        --https-only true \
        2>/dev/null && echo "  ✓ HTTPS enforced: $APP"
done

# Set minimum TLS version
for APP in api ai; do
    az webapp config set \
        --name "app-${PROJECT}-${APP}-${ENVIRONMENT}" \
        --resource-group "$RG" \
        --min-tls-version 1.2 \
        --ftps-state Disabled \
        2>/dev/null && echo "  ✓ TLS 1.2 minimum: $APP"
done

# 4. PostgreSQL security
echo ""
echo "--- Database Security ---"
PSQL_NAME="psql-${PROJECT}-${ENVIRONMENT}"

# Require SSL
az postgres flexible-server parameter set \
    --resource-group "$RG" \
    --server-name "$PSQL_NAME" \
    --name require_secure_transport \
    --value on \
    2>/dev/null && echo "  ✓ SSL required"

# Enable connection throttling
az postgres flexible-server parameter set \
    --resource-group "$RG" \
    --server-name "$PSQL_NAME" \
    --name "connection_throttle.enable" \
    --value on \
    2>/dev/null && echo "  ✓ Connection throttling enabled"

# Enable audit logging
az postgres flexible-server parameter set \
    --resource-group "$RG" \
    --server-name "$PSQL_NAME" \
    --name log_connections \
    --value on \
    2>/dev/null && echo "  ✓ Connection logging enabled"

# 5. Storage security
echo ""
echo "--- Storage Security ---"
STORAGE_NAME="st${PROJECT}${ENVIRONMENT}"

# Require HTTPS
az storage account update \
    --name "$STORAGE_NAME" \
    --resource-group "$RG" \
    --https-only true \
    --min-tls-version TLS1_2 \
    2>/dev/null && echo "  ✓ HTTPS + TLS 1.2 enforced"

# 6. Redis security
echo ""
echo "--- Redis Security ---"
REDIS_NAME="redis-${PROJECT}-${ENVIRONMENT}"

az redis update \
    --name "$REDIS_NAME" \
    --resource-group "$RG" \
    --set "minimumTlsVersion=1.2" \
    2>/dev/null && echo "  ✓ TLS 1.2 minimum"

echo ""
echo "=== Security Configuration Complete ==="
