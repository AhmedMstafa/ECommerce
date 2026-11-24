# API versioning

The API uses URL-segment versioning:

```text
/api/v{version:apiVersion}/[controller]
```

Only `v1.0` is currently supported. The version is exposed as `v1` in Swagger group names and in the URL.

## Behavior

- The default version is `1.0`.
- `api-supported-versions` is returned in responses because `ReportApiVersions` is enabled.
- Requests without a version segment do **not** fall back to v1 because the route template requires the segment. Clients must use `/api/v1/...`.
- Swagger documents are available in Development at `/swagger/index.html`; `Swagger:Enabled=true` can enable them in another environment when needed.

## Adding a new version

1. Add a new controller deriving from `ApiControllerBase` with `[ApiVersion("2.0")]`.
2. Keep v1 controllers unchanged; mark them deprecated only when a migration path exists.
3. Update `ECommerce.API.http` with the new base URL variable.
4. Add integration tests for both versions before removing or deprecating v1.
