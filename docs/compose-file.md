# Compose file support

WSLCC parses the same YAML that Docker Compose uses. The parser lives in
`Wslcc.Core.Compose.ComposeFileParser` and maps into the model in `Wslcc.Abstractions.Compose`.

## Current status

The parser is intentionally **tolerant** and understands the common short/long forms of the
most-used keys:

- `services.<name>`: `image`, `build` (string shorthand or `{ context, dockerfile, target, args }`),
  `container_name`, `command`, `entrypoint`, `environment` (list `KEY=VALUE` or map), `env_file`,
  `ports`, `volumes`, `depends_on` (list or map), `networks` (list or map), `labels`, `restart`,
  `working_dir`, `user`.
- Top-level `name`, `networks`, `volumes` (with `driver`, `external`).

Unknown keys are ignored rather than causing a failure.

## Not yet covered

Full Compose specification fidelity is planned (see [todo.md](todo.md)), including: `profiles`,
`extends`, `configs`/`secrets`, healthchecks, deploy settings, variable interpolation
(`${VAR}` / `.env`), and multi-file merges/overrides. Ports and volumes are currently kept as raw
strings rather than fully structured objects.

## Example

```yaml
name: sample
services:
  web:
    image: nginx:1.27
    ports:
      - "8080:80"
    depends_on:
      - redis
  redis:
    image: redis:7
```

See [../examples](../examples) for runnable samples.
