#!/bin/bash
set -e

ENVIRONMENT="${1:-dev}"
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

echo "=== Guardrail Service Deployment ==="
echo "Environment: $ENVIRONMENT"
echo ""

# Validate environment
if [[ ! "$ENVIRONMENT" =~ ^(dev|staging|prod)$ ]]; then
    echo "ERROR: Invalid environment. Use: dev, staging, prod"
    exit 1
fi

# Check prerequisites
for cmd in az terraform docker; do
    if ! command -v "$cmd" &> /dev/null; then
        echo "ERROR: $cmd is required but not installed"
        exit 1
    fi
done

echo "--- Step 1: Build Services ---"

# Build .NET API
echo "Building API Gateway..."
cd "$PROJECT_DIR/src/api-gateway"
dotnet publish -c Release -o "$PROJECT_DIR/publish/api"

# Build Python AI Service
echo "Building AI Service..."
cd "$PROJECT_DIR/src/ai-service"
pip install -r requirements.txt -q

# Build React Frontend
echo "Building Frontend..."
cd "$PROJECT_DIR/src/frontend"
npm install --silent
npm run build

echo ""
echo "--- Step 2: Infrastructure ---"
cd "$PROJECT_DIR/terraform"

terraform init -input=false

terraform plan \
    -var-file="environments/$ENVIRONMENT/terraform.tfvars" \
    -var="db_password=${DB_PASSWORD}" \
    -out=tfplan

echo ""
read -p "Apply infrastructure changes? (y/n): " confirm
if [ "$confirm" = "y" ]; then
    terraform apply -auto-approve tfplan
fi

echo ""
echo "--- Step 3: Database Migration ---"
for sql_file in "$PROJECT_DIR"/sql/*.sql; do
    echo "Running: $(basename "$sql_file")"
done

echo ""
echo "--- Step 4: Docker Build ---"
if [ "$ENVIRONMENT" != "dev" ]; then
    ACR=$(terraform output -raw container_registry)
    echo "Pushing to: $ACR"

    docker build -t "$ACR/guardrail-api:latest" "$PROJECT_DIR/src/api-gateway"
    docker build -t "$ACR/guardrail-ai:latest" "$PROJECT_DIR/src/ai-service"
    docker build -t "$ACR/guardrail-web:latest" "$PROJECT_DIR/src/frontend"

    az acr login --name "$(echo $ACR | cut -d. -f1)"
    docker push "$ACR/guardrail-api:latest"
    docker push "$ACR/guardrail-ai:latest"
    docker push "$ACR/guardrail-web:latest"
fi

echo ""
echo "--- Deployment Complete ---"
echo "API:      $(terraform output -raw api_url)"
echo "AI:       $(terraform output -raw ai_service_url)"
echo "Frontend: $(terraform output -raw frontend_url)"
