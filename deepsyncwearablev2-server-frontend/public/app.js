const statusGrid = document.getElementById('status-grid');
const simGrid = document.getElementById('sim-grid');
const addBtn = document.getElementById('add-sim-btn');
const connectionStatus = document.getElementById('connection-status');

init().catch((err) => {
  console.error('Failed to initialize dashboard', err);
});

async function init() {
  const urlConfig = await fetch('/config/url-config.json').then((res) => res.json());
  const clientUrls = urlConfig.routes;
  let lastConnected = undefined;
  let lastWearablesKey = '';
  let lastSimulatedKey = '';

  addBtn.addEventListener('click', async () => {
    const res = await fetch(clientUrls.simulatedCreate, { method: 'POST' });
    const payload = await safeJson(res);
    if (payload?.simulatedWearables) {
      renderSimulated(payload.simulatedWearables);
      lastSimulatedKey = JSON.stringify(payload.simulatedWearables);
    }
  });

  async function refresh() {
    const res = await fetch(clientUrls.state);
    const state = await res.json();

    const connected = Boolean(state.serverConnected);
    if (connected !== lastConnected) {
      updateConnectionIndicator(connected);
      lastConnected = connected;
      lastWearablesKey = '';
    }

    const wearablesKey = connected
      ? JSON.stringify(state.wearables ?? [])
      : 'disconnected';
    if (wearablesKey !== lastWearablesKey) {
      const wearableItems = connected ? state.wearables : [];
      renderWearables(wearableItems, connected);
      lastWearablesKey = wearablesKey;
    }

    const nextSimKey = JSON.stringify(state.simulatedWearables ?? []);
    if (nextSimKey !== lastSimulatedKey) {
      renderSimulated(state.simulatedWearables);
      lastSimulatedKey = nextSimKey;
    }
  }

  function renderWearables(items, connected) {
    statusGrid.innerHTML = '';
    if (!connected) {
      statusGrid.innerHTML = '<p class="infotext">DeepSync server disconnected.</p>';
      return;
    }
    if (!items || !items.length) {
      statusGrid.innerHTML = '<p class="infotext">No wearables connected.</p>';
      return;
    }

    items.forEach((w) => {
      const card = document.createElement('div');
      card.className = 'card';
      card.innerHTML = `
        <strong>ID: ${w.id}</strong>
        <div class="row"><span>Heart rate</span><span>${w.heartRate}</span></div>
        <div class="row"><span>Color</span><span class="color-swatch" style="background: ${toHex(w.color)}"></span></div>
        <div class="row"><span>Timestamp</span><span>${w.timestamp ?? ''}</span></div>
      `;
      statusGrid.appendChild(card);
    });
  }

  function renderSimulated(items) {
    simGrid.innerHTML = '';
    if (!items || !items.length) {
      simGrid.innerHTML = '<p class="infotext">No simulated wearables.</p>';
      return;
    }

    items.forEach((sim) => {
      const card = document.createElement('div');
      card.className = 'editable-card';
      card.innerHTML = `
        <strong>${sim.ip ?? '—'}</strong>
        ${renderEditableRow('ID', 'id', sim.id)}
        ${renderEditableRow('Base HR', 'baseHeartRate', sim.baseHeartRate)}
        ${renderEditableRow('Amplitude', 'amplitude', sim.amplitude)}
        ${renderEditableRow('Speed Hz', 'speedHz', sim.speedHz, 0.1)}
        ${renderEditableRow('Interval ms', 'intervalMs', sim.intervalMs, 10)}
        <div class="row editable-row">
          <span>Color</span>
          <div class="input-group">
            <span class="color-swatch" style="background: ${toHex(sim.color)}"></span>
            <input type="color" name="color" value="${toHex(sim.color)}" />
          </div>
        </div>
        <div class="card-actions column">
          <button data-action="update">Update</button>
          <button class="secondary" data-action="delete">Remove</button>
        </div>
      `;

      const updateBtn = card.querySelector('button[data-action="update"]');
      const deleteBtn = card.querySelector('button[data-action="delete"]');

      updateBtn.addEventListener('click', async () => {
        const ip = normalizeIp(sim.ip);
        if (!ip) {
          alert('Simulated wearable is missing an IP address.');
          return;
        }

        const payload = buildSimPayload(card, sim, ip);
        const updateUrl = buildActionUrl(clientUrls.simulatedUpdate, ip);
        await fetch(updateUrl, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        await refresh();
      });

      deleteBtn.addEventListener('click', async () => {
        const ip = normalizeIp(sim.ip);
        if (!ip) {
          alert('Simulated wearable is missing an IP address.');
          return;
        }
        const deleteUrl = buildActionUrl(clientUrls.simulatedDelete, ip);
        await fetch(deleteUrl, { method: 'DELETE' });
        await refresh();
      });

      simGrid.appendChild(card);
    });
  }

  await refresh();
  setInterval(refresh, 50);
}

async function safeJson(response) {
  try {
    return await response.json();
  } catch (err) {
    console.warn('Failed to parse JSON response', err);
    return null;
  }
}

function updateConnectionIndicator(connected) {
  if (!connectionStatus) {
    return;
  }
  connectionStatus.textContent = connected ? 'DeepSync Server Connected' : 'DeepSync Server Disconnected';
  connectionStatus.classList.toggle('connection-indicator--connected', connected);
  connectionStatus.classList.toggle('connection-indicator--disconnected', !connected);
}

function toHex(color) {
  if (!color) return '#000000';
  const r = color.r.toString(16).padStart(2, '0');
  const g = color.g.toString(16).padStart(2, '0');
  const b = color.b.toString(16).padStart(2, '0');
  return '#' + r + g + b;
}

function fromHex(hex) {
  const clean = hex.replace('#', '');
  return {
    r: parseInt(clean.substring(0,2), 16),
    g: parseInt(clean.substring(2,4), 16),
    b: parseInt(clean.substring(4,6), 16)
  };
}

function formatValue(value) {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  return value;
}

function renderEditableRow(label, name, value, step = 1, inputType = 'number') {
  const stepAttr = inputType === 'number' && step !== null && step !== undefined ? `step="${step}"` : '';
  const inputValue = value ?? '';
  return `
    <div class="editable-row">
      <span>${label}</span>
      <div class="input-group">
        <span>${formatValue(value)}</span>
        <input type="${inputType}" name="${name}" ${stepAttr} value="${inputValue}" />
      </div>
    </div>
  `;
}

function buildSimPayload(card, sim, ip) {
  const getValue = (name, fallback) => {
    const input = card.querySelector(`input[name="${name}"]`);
    if (!input) return fallback;
    if (input.type === 'number') {
      const num = Number(input.value);
      return Number.isFinite(num) ? num : fallback;
    }
    return input.value?.trim() ?? fallback;
  };

  const colorInput = card.querySelector('input[name="color"]');
  const colorHex = colorInput?.value ?? toHex(sim.color);

  return {
    ip,
    id: getValue('id', sim.id),
    baseHeartRate: getValue('baseHeartRate', sim.baseHeartRate),
    amplitude: getValue('amplitude', sim.amplitude),
    speedHz: getValue('speedHz', sim.speedHz),
    intervalMs: getValue('intervalMs', sim.intervalMs),
    color: fromHex(colorHex)
  };
}

function normalizeIp(value) {
  return typeof value === 'string' ? value.trim() : '';
}

function buildActionUrl(template, identifier) {
  if (!identifier) {
    return template;
  }
  const encoded = encodeURIComponent(identifier);
  if (template.includes(':ip')) {
    return template.replace(':ip', encoded);
  }
  if (template.includes(':id')) {
    return template.replace(':id', encoded);
  }
  return template;
}
