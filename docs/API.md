# REST API

Base URL in development: `http://localhost:5080`.

```powershell
curl.exe -X POST http://localhost:5080/api/v1/extractions -F "file=@Samples/Invoice - Kopi Pagi.pdf"
curl.exe http://localhost:5080/api/v1/extractions?limit=20
curl.exe http://localhost:5080/api/v1/extractions/{id}
curl.exe http://localhost:5080/health
```

When `Security:ApiKey` is configured, include `X-Api-Key`. A successful upload returns HTTP 201 and a result containing status, source method, SHA-256, timings, warnings, and field-level raw/normalized values, confidence, page, method, and evidence. The persisted `fields.Currency` value is `USD` when a `$` marker is present; otherwise it defaults to `IDR`.
