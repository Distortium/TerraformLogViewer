# TerraformLogViewer
Хакатон Т1

Запуск:
docker-compose up --build

Примеры использования API:

### Загрузка файла через curl

curl -X POST \
  https://your-api.com/api/v1/logs/upload \
  -F "file=@terraform.log" \
  -F "fileName=production-deploy.log" \
  -F "fileType=JSON" \
  -F "userId=optional-user-id"

### Поиск логов
curl -X POST \
  https://your-api.com/api/v1/logs/{logFileId}/search \
  -H "Content-Type: application/json" \
  -d '{
    "freeText": "error",
    "minLogLevel": "Error",
    "phase": "Apply",
    "page": 1,
    "pageSize": 10
  }'

### Создание алерта
curl -X POST \
  https://your-api.com/api/v1/logs/{logFileId}/alerts \
  -H "Content-Type: application/json" \
  -d '{
    "alertId": "high-error-rate",
    "title": "High Error Rate Detected",
    "description": "Found 15 errors in Terraform apply",
    "severity": "Error",
    "logEntryIds": ["entry-id-1", "entry-id-2"]
  }'