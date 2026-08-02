# Examples

Sample Compose projects for WSLCC. These use standard Compose YAML, so they also work with `docker compose` (handy for comparing behavior).

## `web-redis/`

A minimal two-service app (nginx + redis) demonstrating `image`, `ports`, and `depends_on`.

```powershell
# Inspect the active provider / engine version
wslcc compose version

# Bring the stack up/down (options follow the leaf command)
wslcc compose up --project-directory examples/web-redis -d
wslcc compose ps --project-directory examples/web-redis
wslcc compose down --project-directory examples/web-redis
```

See [../docs/compose-file.md](../docs/compose-file.md) for which Compose keys are currently supported, and [../docs/compatibility.md](../docs/compatibility.md) if you are migrating from `docker compose`.
