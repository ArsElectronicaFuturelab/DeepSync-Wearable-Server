# DeepSync Wearable Frontend

A lightweight Node/Hono bridge that fronts the DeepSync wearable C# backend. It serves a minimal dashboard, collects wearable snapshots, and forwards simulated control actions to the backend control API. Use this alongside the C# server described in the [Server README](../deepsyncwearablev2-server/README.md).

## Features

- Serves the dashboard assets from `public/` and exposes the current wearable as well as simulated state via REST.
- Accepts wearable snapshots from the backend (`/api/wearables`) and keeps them fresh with a timeout guard.
- Proxies simulated wearable control actions to the backend control endpoint and applies returned snapshots.
- CORS enabled for easy local testing.

## Requirements

- Node.js 18+ (tested with npm)

## Install & Run (dev)

1. Install dependencies: `npm install`
2. Start the dev server (tsx watch): `npm run dev`
3. Open the dashboard: http://localhost:8788

Production run

1. Build: `npm run build`
2. Start compiled server: `npm start`

## Configuration

- Configuration file: `config/url-config.json`
  - `routes` defines all REST paths the backend uses to push data and receive state.
  - `server.backendControlUrl` default points to the C# control endpoint (e.g. `http://localhost:8790/api/control`).
- Environment variables (optional)
  - `PORT` (default `8788`): HTTP port for this service.
  - `BACKEND_CONTROL_URL`: Overrides the backend control URL.
  - `CONTROL_TIMEOUT_MS` (default `5000`): Timeout for control calls to the backend.
  - `WEARABLE_TIMEOUT_MS` (default `1000`): Idle timeout before clearing stale wearable data.

Key endpoints (matching the C# server expectations)

- POST `/api/wearables`: backend pushes live wearable snapshot(s).
- GET `/api/state`: returns `{ wearables, simulatedWearables, serverConnected }` used by the dashboard.
- POST `/api/wearables/simulated`: backend pushes the simulated snapshot array.
- POST `/api/control/simulated/create`: proxy create request to backend control.
- POST `/api/control/simulated/:ip/update`: proxy update request; returns updated simulated snapshot.
- DELETE `/api/control/simulated/:ip`: proxy delete request; returns updated simulated snapshot.

## Development notes

- Static assets are served with no-store caching for live reload friendliness.
- If you change routes or the control URL, keep them aligned with the C# server so it can push snapshots and receive control commands successfully.

## License

MIT License - See [LICENSE](../LICENSE) for details.

## Credits

**Developed by:** Ars Electronica Futurelab

**Website:** https://ars.electronica.art/futurelab/
