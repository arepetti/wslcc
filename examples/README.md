# Examples

Sample Compose projects for WSLCC. These use standard Compose YAML, so they also work with `docker compose` (handy for comparing behavior while WSLCC's compose lifecycle is built out).

## `web-redis/`

A minimal two-service app (nginx + redis) demonstrating `image`, `ports`, and `depends_on`.

```powershell
# Inspect the active provider / engine version
wslcc compose version

# (Once implemented) bring the stack up/down:
# wslcc compose --project-directory examples/web-redis up
# wslcc compose --project-directory examples/web-redis down
```

See [../docs/compose-file.md](../docs/compose-file.md) for which Compose keys are currently supported.
