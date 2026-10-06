# MongoDB and a web UI

A small three-container stack you can bring up to check that a basic WSLCC setup works, then open in a browser.

## What is inside

- **mongo** — a MongoDB 7 database. Its data is stored in a named volume (`mongo-data`), so it is still there after `down` unless you also delete volumes. Other containers reach it on the `data` network as `database`.
- **seed** — a short job that uses the same MongoDB image. It waits until MongoDB is healthy, writes one note into the `demo` database, and exits.
- **ui** — [mongo-express](https://github.com/mongo-express/mongo-express), a web page for browsing the database. It starts only after MongoDB is healthy and the seed job has finished. The page is published on port **8081**.

Login for both MongoDB and the web page is username `demo`, password `demo`.

## What this exercises

Image pulls, a published port, environment variables, a named volume, a user-defined network with an alias, a health check, `depends_on` with `service_healthy` and `service_completed_successfully`, a one-shot `command`, `hostname`, and `stop_grace_period`.

Restart policies are left out so the same file can run on the WSL containers provider and on Docker.

## Try it

Start the daemon once if it is not already running (`wslcc daemon start`), then from the repo root:

```powershell
wslcc compose up --project-directory examples/mongo-ui -d
```

The first run pulls the images and waits until MongoDB is healthy and the seed job exits. That can take a minute or two. When it returns:

```powershell
wslcc compose ps --project-directory examples/mongo-ui
```

`mongo` and `ui` should be running. `seed` should have exited successfully.

Open [http://localhost:8081](http://localhost:8081) and sign in as `demo` / `demo`. Open the **demo** database and the **notes** collection. You should see one document whose message is `Hello from the WSLCC example`.

Stop the stack:

```powershell
wslcc compose down --project-directory examples/mongo-ui
```

Add `-v` on that command when you also want the database volume removed.
