# DeepSync Wearable Server

Monorepo-style layout containing the C# TCP/REST backend and the lightweight Node/Hono frontend bridge.

## Folder structure

- `deepsyncwearablev2-server-server/`  
  - C# backend that handles wearable TCP connections, relays data, and exposes control endpoints.
  - See [Server README](deepsyncwearablev2-server/README.md) for more information
- `deepsyncwearablev2-server-frontend/`
  - Node/Hono service that serves the dashboard, receives wearable snapshots, and forwards simulated control actions to the backend.
  - See [Frontend README](deepsyncwearablev2-server-frontend/README.md) for more information

## What each part does

- Backend (C#):
  - Listens for wearable devices and application clients over TCP.
  - Publishes wearable data and accepts control commands via REST (`/api/wearables`, `/api/wearables/simulated`, `/api/control`).
  - Provides simulation mode and LED color configuration support.
- Frontend (Node/Hono):
  - Serves the dashboard UI from `public/` and exposes the current wearable state via REST.
  - Accepts POSTed wearable snapshots from the backend and keeps them fresh with a timeout guard.
  - Proxies simulated wearable control actions to the backend control endpoint.

## Quick start

1. Backend: follow `deepsyncwearablev2-server-server/README.md` (requires .NET 8+).
2. Frontend: follow `deepsyncwearablev2-server-frontend/README.md` (requires Node 18+).
3. Ensure the backend control endpoint URL in `deepsyncwearablev2-server-frontend/config/url-config.json` matches your backend (`http://localhost:8790/api/control` by default). The frontend listens on `http://localhost:8788` by default.

## Notes

- Keep route definitions in the frontend config aligned with the backend so snapshot posts and control calls succeed.

## License

MIT License - See [LICENSE](LICENSE) for details.

## Credits

**Developed by:** Ars Electronica Futurelab

**Website:** https://ars.electronica.art/futurelab/
