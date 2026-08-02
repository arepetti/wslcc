# Troubleshooting

Common first-run and day-to-day failures. For flag and file semantics, see [cli-mapping.md](cli-mapping.md) and [compose-file.md](compose-file.md).

## Daemon not reachable

```text
Daemon not reachable. Start it with wslcc daemon start.
```

**Most common cause: elevation mismatch.** The named pipe only accepts clients that match the daemon's **Windows user and elevation level**. An elevated (Administrator) shell cannot talk to a non-elevated `wslccd`, and vice versa. `wslcc daemon status` from a *matching* shell may say the daemon is running while the other shell still reports unreachable.

**What to do:**

1. Run `wslcc daemon status` in the same kind of shell you use for compose commands (both elevated, or both not).
2. If they disagree, stop the daemon from the shell that can see it (`wslcc daemon stop`), then start it from the shell you will use (`wslcc daemon start`).
3. If status fails in every shell, start the daemon: `wslcc daemon start`.
4. Confirm `wslccd` is on disk: set `WSLCCD_PATH` or place it next to `wslcc.exe` (see [daemon.md](daemon.md)).

Background: [SECURITY.md](../SECURITY.md).

## `up` seems hung for minutes

`depends_on` conditions `service_healthy` and `service_completed_successfully` wait up to **5 minutes** (hard-coded, not configurable). Until that deadline, attached `up` can look stuck with no progress.

- Check whether a dependency never becomes healthy: `wslcc compose ps -a` and `wslcc compose logs <service>`.
- Fix or temporarily drop the health condition, or set `required: false` on that dependency.
- After five minutes you should see a timeout error naming the dependency — see [compose-file.md#startup-order-and-health](compose-file.md#startup-order-and-health).

## No compose file found

```text
No compose file found. Use -f <path> or run from a directory containing compose.yaml / docker-compose.yml.
```

- Pass `-f` / `--file`, or `cd` into the project directory, or set `COMPOSE_FILE`.
- Put options **after** the leaf command. `wslcc compose --project-directory examples/web-redis up` does **not** apply `--project-directory` — use `wslcc compose up --project-directory examples/web-redis`. See [cli-mapping.md#global-options](cli-mapping.md#global-options).

## Provider row is `unavailable`

`wslcc version` (or `compose version`) shows a provider as unavailable when its tool is missing or unreachable.

- **`wslc`:** install/update WSL pre-release (`wsl --update --pre-release`) so `wslc` is on `PATH`.
- **`docker`:** install Docker and ensure `docker` works in the same session.
- Override for one command: `wslcc compose … --wslcc-provider docker` (or `wslc`). Set the daemon default with `wslcc daemon start --provider …`.

## Long-form `ports:` / `volumes:` rejected

```text
service '…': 'ports' long map form is not supported; use short syntax …
```

Only short syntax is accepted (`"8080:80"`, `./data:/data`). Rewrite map-form entries; details in [compose-file.md](compose-file.md#service-reference).

## Wrong values from `.env`

Without `--env-file` or `--project-directory`, the default `.env` is read from the **current working directory**, not automatically from the first `-f` file's folder. Example: `wslcc compose up -f apps/web/compose.yaml` from the repo root uses `./.env`, not `apps/web/.env`. Pass `--project-directory apps/web` (or `--env-file`) when you need the project-local file. See [compose-file.md#resolution-features](compose-file.md#resolution-features).

## Containers missing from `docker compose ps`

WSLCC labels containers `wslcc.project` / `wslcc.service`. Even with `--wslcc-provider docker`, they are **not** managed by Docker Compose and will not appear in `docker compose ps` (and Compose projects will not appear in `wslcc compose ps`). Use `wslcc compose ps` / `docker ps` instead. Full migration notes: [compatibility.md](compatibility.md#interop-projects-do-not-share-a-world).

## See more detail on an RPC failure

Set `WSLCC_DEBUG=1` before a compose command to print the full gRPC exception instead of the one-line summary.
