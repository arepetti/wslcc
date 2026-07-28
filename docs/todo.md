# TODO / Deferred work

Tracked, intentionally-deferred work. See [roadmap.md](roadmap.md) for the big picture.

## Managed API NuGet package

- Publish a managed API package (proposed: `Wslcc.Api`) that exposes WSLCC's capabilities to other .NET applications as a clean, idiomatic client library — so consumers do not have to talk to the daemon's gRPC surface directly.
- It should wrap `Wslcc.Client` (channel + generated contracts) behind friendly async types and hide the transport (named pipe vs HTTP).

## Compose engine

- `config --resolve-image-digests`: pin each service image to its `repo@sha256:...` digest. Deferred because `config` runs offline/client-side and resolving digests needs registry access (would require a registry client or routing through the daemon/provider).
- Stream per-service progress for the remaining unary RPCs (`Up`/`Down`/`Ps`/`Start`/`Stop`/`Restart`/`Pull`/`Build`) instead of returning a single batch result, so long operations report progress incrementally (as `logs` and attached `up` already do).
- `logs --follow`: a live stream is still interleaved as lines arrive (only a bounded, non-follow dump is sorted by timestamp). Ordering a running multi-container stream would need a bounded reorder buffer / watermark delay.
- Networks/volumes: only the common attributes are modeled (`driver`, `external`); per-network settings (`ipam`/`ipv4_address`/subnets, extra volume `driver_opts`) and an explicit resource `name:` are not.

## Compose file fidelity

- Multi-file merge dedups exact-duplicate sequence entries; Compose's per-resource unique-key merge for a few list attributes (e.g. long-form `ports`/`volumes` by target) is not modeled.
- Still unsupported: `configs`/`secrets` and `deploy` settings.
- Structured `ports`/`volumes` models instead of raw strings; only the short syntax is parsed for either.
- `container_name`, `user`, `working_dir`, `labels`, and `entrypoint` are parsed into `ServiceSpec` but never applied to the container (see `ComposeEngine.ToRunSpec`); a service that sets any of these gets no error, just no effect. `env_file` is parsed but its files are never read into the container either.
- `command:` written as a single string (Compose's shell-form shorthand) is passed through as one argv token instead of being shell-split/wrapped the way `docker compose` runs it — it only works for a bare, argument-less executable name. The list (exec) form is unaffected. See [compose-file.md#command-and-entrypoint](compose-file.md#command-and-entrypoint).

## WSL provider

- Enable the `WSLC_SDK` path and move from the `wslc.exe` fallback to the managed `Microsoft.WSL.Containers` SDK as it reaches API parity; then remove the CLI fallback.

## Daemon / remote

- Authentication and TLS for the remote HTTP/2 endpoint (currently unauthenticated/unencrypted).

## GUI

- WinUI3 app to visualize images/containers, status, metrics, and logs, talking to `wslccd` over gRPC.
