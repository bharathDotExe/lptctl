/**
 * LaptopCtl Web Dashboard Client Application
 * Connects to LaptopCtl Backend API (REST + WebSocket)
 */

let savedBackend = localStorage.getItem('laptopctl_backend_url');
const API_BASE = savedBackend || (
  window.location.origin.includes(':5173') ||
  window.location.origin.includes(':3000') ||
  window.location.protocol === 'file:' ||
  window.location.hostname.includes('vercel.app')
    ? 'http://localhost:5000'
    : window.location.origin
);

// Clear any legacy persistent tokens from localStorage to prevent auto-login
localStorage.removeItem('laptopctl_token');
localStorage.removeItem('laptopctl_user_email');

const state = {
  activeTab: 'devices',
  devices: [],
  selectedDeviceId: null,
  currentDirectory: 'C:\\',
  jwtToken: sessionStorage.getItem('laptopctl_token'),
  currentUser: { email: sessionStorage.getItem('laptopctl_user_email') || '' },
  mockMode: false,
  authMode: 'login'
};

// Initial Mock Data (used if backend is offline or for preview)
const MOCK_DEVICES = [
  {
    deviceId: 'win-7f9a2b1c',
    deviceName: 'WORKSTATION-ENG-01',
    osVersion: 'Windows 11 Enterprise (Build 22631.3447)',
    osArchitecture: 'X64',
    agentVersion: '1.0.0',
    isOnline: true,
    lastSeen: new Date(Date.now() - 1000 * 12).toISOString(),
    enrolledAt: new Date(Date.now() - 1000 * 86400 * 14).toISOString(),
    enrollmentStatus: 'Active'
  },
  {
    deviceId: 'win-4e8c3a9d',
    deviceName: 'LAPTOP-EXEC-04',
    osVersion: 'Windows 11 Pro (Build 22631.3296)',
    osArchitecture: 'X64',
    agentVersion: '1.0.0',
    isOnline: true,
    lastSeen: new Date(Date.now() - 1000 * 25).toISOString(),
    enrolledAt: new Date(Date.now() - 1000 * 86400 * 5).toISOString(),
    enrollmentStatus: 'Active'
  },
  {
    deviceId: 'win-9b1d5c2e',
    deviceName: 'DEV-RIG-ALPHA',
    osVersion: 'Windows 10 Pro (Build 19045.4291)',
    osArchitecture: 'X64',
    agentVersion: '1.0.0',
    isOnline: false,
    lastSeen: new Date(Date.now() - 1000 * 3600 * 8).toISOString(),
    enrolledAt: new Date(Date.now() - 1000 * 86400 * 30).toISOString(),
    enrollmentStatus: 'Active'
  }
];

const MOCK_DIAGNOSTICS = {
  computerName: 'WORKSTATION-ENG-01',
  windowsVersion: 'Windows 11 Enterprise 23H2 (Build 22631.3447)',
  osArchitecture: '64-bit Operating System, x64-based processor',
  agentVersion: '1.0.0-prod',
  cpuModel: '13th Gen Intel(R) Core(TM) i9-13900K @ 3.00 GHz',
  totalMemoryBytes: 34359738368, // 32 GB
  lastConnection: new Date().toISOString(),
  drives: [
    { name: 'C:\\', volumeLabel: 'OS_NVME', driveFormat: 'NTFS', totalBytes: 1024 * 1024 * 1024 * 1000, availableBytes: 1024 * 1024 * 1024 * 420 },
    { name: 'D:\\', volumeLabel: 'DATA_PROJECTS', driveFormat: 'NTFS', totalBytes: 1024 * 1024 * 1024 * 2000, availableBytes: 1024 * 1024 * 1024 * 1350 }
  ],
  network: [
    { name: 'Ethernet 1', description: 'Intel(R) Ethernet Controller I225-V', status: 'Up', addresses: ['192.168.1.145', 'fe80::4a21:99bf:12c4'] },
    { name: 'Wi-Fi', description: 'Intel(R) Wi-Fi 6E AX211 160MHz', status: 'Up', addresses: ['192.168.1.189'] }
  ],
  processes: [
    { processId: 1044, name: 'LaptopCtlAgent', description: 'LaptopCtl Remote Administration Agent', workingSetBytes: 42 * 1024 * 1024, startTime: new Date(Date.now() - 3600000 * 12).toISOString() },
    { processId: 4, name: 'System', description: 'NT Kernel & System', workingSetBytes: 185 * 1024 * 1024, startTime: new Date(Date.now() - 3600000 * 72).toISOString() },
    { processId: 780, name: 'svchost.exe', description: 'Host Process for Windows Services', workingSetBytes: 95 * 1024 * 1024, startTime: new Date(Date.now() - 3600000 * 72).toISOString() },
    { processId: 3210, name: 'explorer.exe', description: 'Windows Explorer', workingSetBytes: 240 * 1024 * 1024, startTime: new Date(Date.now() - 3600000 * 14).toISOString() },
    { processId: 9140, name: 'powershell.exe', description: 'Windows PowerShell', workingSetBytes: 68 * 1024 * 1024, startTime: new Date(Date.now() - 3600000 * 2).toISOString() }
  ]
};

const MOCK_FILES = [
  { name: 'Documents', fullPath: 'C:\\Users\\Public\\Documents', isDirectory: true, sizeBytes: null, lastModified: new Date().toISOString() },
  { name: 'Downloads', fullPath: 'C:\\Users\\Public\\Downloads', isDirectory: true, sizeBytes: null, lastModified: new Date().toISOString() },
  { name: 'system_health_report.log', fullPath: 'C:\\Users\\Public\\system_health_report.log', isDirectory: false, sizeBytes: 14250, lastModified: new Date().toISOString() },
  { name: 'agent_diagnostics.json', fullPath: 'C:\\Users\\Public\\agent_diagnostics.json', isDirectory: false, sizeBytes: 8120, lastModified: new Date().toISOString() }
];

const MOCK_AUDIT = [
  { eventId: 'evt-109', eventType: 'enrollment.device', deviceId: 'win-7f9a2b1c', success: true, ipAddress: '198.51.100.42', description: "Device 'WORKSTATION-ENG-01' enrolled successfully", occurredAt: new Date(Date.now() - 3600000 * 2).toISOString() },
  { eventId: 'evt-108', eventType: 'session.start', deviceId: 'win-7f9a2b1c', success: true, ipAddress: '198.51.100.42', description: "Remote administration session opened", occurredAt: new Date(Date.now() - 3600000 * 3).toISOString() },
  { eventId: 'evt-107', eventType: 'auth.login', deviceId: null, success: true, ipAddress: '203.0.113.19', description: "Administrator login from controller", occurredAt: new Date(Date.now() - 3600000 * 4).toISOString() },
  { eventId: 'evt-106', eventType: 'command.exec', deviceId: 'win-4e8c3a9d', success: true, ipAddress: '198.51.100.42', description: "Executed: Get-Service LaptopCtlAgent", occurredAt: new Date(Date.now() - 3600000 * 6).toISOString() }
];

// Initialize application
document.addEventListener('DOMContentLoaded', () => {
  setupNavigation();
  setupEventListeners();
  initAuthUI();

  if (state.jwtToken) {
    loadDevices();
    loadAuditLog();
  } else {
    renderDevicesTable([]);
    updateMetrics();
  }

  // Auto-refresh devices and metrics every 5 seconds if authenticated
  setInterval(() => {
    if (state.jwtToken) {
      loadDevices();
    }
  }, 5000);
});

// Tab Navigation
function setupNavigation() {
  const tabs = document.querySelectorAll('.nav-item');
  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      const targetTab = tab.getAttribute('data-tab');
      switchTab(targetTab);
    });
  });

  document.getElementById('btn-quick-enroll').addEventListener('click', () => {
    switchTab('enroll');
  });
}

function switchTab(tabId) {
  state.activeTab = tabId;
  document.querySelectorAll('.nav-item').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));

  const btn = document.getElementById(`tab-btn-${tabId}`);
  const pane = document.getElementById(`pane-${tabId}`);
  if (btn) btn.classList.add('active');
  if (pane) pane.classList.add('active');

  if (tabId === 'files') {
    loadFiles();
  }
}

// Event Listeners
function setupEventListeners() {
  document.getElementById('btn-refresh-devices').addEventListener('click', loadDevices);
  document.getElementById('btn-refresh-audit').addEventListener('click', loadAuditLog);
  document.getElementById('btn-fetch-diagnostics').addEventListener('click', refreshDiagnostics);
  document.getElementById('btn-generate-code').addEventListener('click', generateEnrollmentCode);
  document.getElementById('btn-copy-command').addEventListener('click', copyEnrollmentCommand);

  // Auth event listeners
  const btnOpenAuth = document.getElementById('btn-open-auth');
  if (btnOpenAuth) btnOpenAuth.addEventListener('click', () => openAuthModal());

  const btnCloseAuth = document.getElementById('btn-close-auth');
  if (btnCloseAuth) btnCloseAuth.addEventListener('click', closeAuthModal);

  const authModal = document.getElementById('auth-modal');
  if (authModal) {
    authModal.addEventListener('click', (e) => {
      if (e.target === authModal) closeAuthModal();
    });
  }

  const btnLogout = document.getElementById('btn-logout');
  if (btnLogout) {
    btnLogout.addEventListener('click', () => {
      sessionStorage.removeItem('laptopctl_token');
      sessionStorage.removeItem('laptopctl_user_email');
      localStorage.removeItem('laptopctl_token');
      localStorage.removeItem('laptopctl_user_email');
      state.jwtToken = null;
      state.currentUser = { email: '' };
      initAuthUI();
      loadDevices();
      loadAuditLog();
    });
  }

  const authForm = document.getElementById('auth-form');
  const authMsg = document.getElementById('auth-msg');
  const btnSubmit = document.getElementById('btn-auth-submit');

  if (authForm) {
    authForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const email = document.getElementById('auth-email').value.trim();
      const password = document.getElementById('auth-password').value;
      if (authMsg) authMsg.style.display = 'none';
      if (btnSubmit) btnSubmit.disabled = true;

      try {
        const loginRes = await fetch(`${API_BASE}/api/auth/login`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email, password })
        });
        const loginData = await loginRes.json();
        if (!loginRes.ok) {
          throw new Error(loginData.message || 'Invalid administrator credentials.');
        }

        const token = loginData.accessToken || loginData.token;
        if (!token) throw new Error('No access token received from server.');

        sessionStorage.setItem('laptopctl_token', token);
        sessionStorage.setItem('laptopctl_user_email', loginData.email || email);
        state.jwtToken = token;
        state.currentUser = { email: loginData.email || email };

        initAuthUI();
        closeAuthModal();
        loadDevices();
        loadAuditLog();
      } catch (err) {
        if (authMsg) {
          authMsg.className = 'auth-msg error';
          authMsg.textContent = err.message || 'Authentication error';
          authMsg.style.display = 'block';
        }
      } finally {
        if (btnSubmit) btnSubmit.disabled = false;
      }
    });
  }

  const indicator = document.getElementById('backend-indicator');
  if (indicator) {
    indicator.style.cursor = 'pointer';
    indicator.title = 'Click to configure backend URL';
    indicator.addEventListener('click', () => {
      const current = localStorage.getItem('laptopctl_backend_url') || API_BASE;
      const url = prompt('Enter LaptopCtl Backend URL (e.g. https://api.yourdomain.com):', current);
      if (url && url.trim()) {
        localStorage.setItem('laptopctl_backend_url', url.trim().replace(/\/$/, ''));
        window.location.reload();
      }
    });
  }

  // Device dropdowns synchronization
  const inspectorSelect = document.getElementById('inspector-device-select');
  const terminalSelect  = document.getElementById('terminal-device-select');
  const filesSelect     = document.getElementById('files-device-select');
  const rcSelect        = document.getElementById('rc-device-select');

  [inspectorSelect, terminalSelect, filesSelect, rcSelect].filter(Boolean).forEach(select => {
    select.addEventListener('change', (e) => {
      state.selectedDeviceId = e.target.value;
      if (inspectorSelect) inspectorSelect.value = state.selectedDeviceId;
      if (terminalSelect) terminalSelect.value = state.selectedDeviceId;
      if (filesSelect) filesSelect.value = state.selectedDeviceId;
      if (rcSelect) rcSelect.value = state.selectedDeviceId;

      if (state.activeTab === 'inspector') refreshDiagnostics();
      if (state.activeTab === 'files') loadFiles();
    });
  });

  initRemoteControl();

  // Terminal submission
  const terminalForm = document.getElementById('terminal-form');
  terminalForm.addEventListener('submit', handleTerminalCommand);
  document.getElementById('btn-clear-terminal').addEventListener('click', () => {
    document.getElementById('terminal-output').innerHTML = '';
  });

  // File explorer go & navigation
  document.getElementById('btn-path-go').addEventListener('click', loadFiles);
  document.getElementById('files-current-path').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      loadFiles();
    }
  });
  document.getElementById('btn-path-up').addEventListener('click', navigateUp);

  document.querySelectorAll('.btn-quick-nav').forEach(btn => {
    btn.addEventListener('click', () => {
      const path = btn.getAttribute('data-path') || 'drives';
      enterDirectory(path);
    });
  });

  // Search input filter
  document.getElementById('device-search-input').addEventListener('input', (e) => {
    const query = e.target.value.toLowerCase();
    renderDevicesTable(state.devices.filter(d =>
      d.deviceName.toLowerCase().includes(query) ||
      d.deviceId.toLowerCase().includes(query) ||
      d.osVersion.toLowerCase().includes(query)
    ));
  });
}

async function loadDevices() {
  if (!state.jwtToken) {
    state.devices = [];
    renderDevicesTable(state.devices);
    updateMetrics();
    updateBackendIndicator(true);
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/api/devices`, {
      headers: { 'Authorization': `Bearer ${state.jwtToken}` }
    });
    if (!res.ok) throw new Error('API offline');
    const data = await res.json();
    state.devices = data.items || [];
    state.mockMode = false;
    updateBackendIndicator(true);
  } catch (err) {
    state.devices = [];
    state.mockMode = false;
    updateBackendIndicator(true);
  }

  renderDevicesTable(state.devices);
  updateMetrics();
  populateDeviceDropdowns();
}

function initAuthUI() {
  const userProfile = document.getElementById('user-profile');
  const btnOpenAuth = document.getElementById('btn-open-auth');
  const userEmail = document.getElementById('header-user-email');
  const userAvatar = document.getElementById('header-avatar');

  if (state.jwtToken) {
    if (userProfile) userProfile.style.display = 'flex';
    if (btnOpenAuth) btnOpenAuth.style.display = 'none';
    const email = sessionStorage.getItem('laptopctl_user_email') || 'Admin';
    if (userEmail) userEmail.textContent = email;
    if (userAvatar) userAvatar.textContent = email.substring(0, 2).toUpperCase();
  } else {
    if (userProfile) userProfile.style.display = 'none';
    if (btnOpenAuth) btnOpenAuth.style.display = 'inline-flex';
  }
}

function openAuthModal(msg = null) {
  const modal = document.getElementById('auth-modal');
  const msgEl = document.getElementById('auth-msg');
  if (msgEl) {
    if (msg) {
      msgEl.className = 'auth-msg error';
      msgEl.textContent = msg;
      msgEl.style.display = 'block';
    } else {
      msgEl.style.display = 'none';
    }
  }
  if (modal) modal.style.display = 'flex';
}

function closeAuthModal() {
  const modal = document.getElementById('auth-modal');
  if (modal) modal.style.display = 'none';
}

function updateBackendIndicator(isOnline) {
  const dot = document.querySelector('.status-dot');
  const txt = document.getElementById('backend-status-text');
  if (isOnline) {
    dot.style.backgroundColor = 'var(--accent-green)';
    txt.textContent = 'Backend: Connected (Live)';
  } else {
    dot.style.backgroundColor = 'var(--accent-amber)';
    txt.textContent = 'Backend: Standalone / Preview Mode';
  }
}

function updateMetrics() {
  const total = state.devices.length;
  const online = state.devices.filter(d => d.isOnline).length;
  document.getElementById('stat-total-devices').textContent = total;
  document.getElementById('stat-online-devices').textContent = online;
  document.getElementById('stat-active-sessions').textContent = online > 0 ? 1 : 0;
  document.getElementById('badge-device-count').textContent = total;
}

function renderDevicesTable(devices) {
  const tbody = document.getElementById('devices-tbody');
  tbody.innerHTML = '';

  if (devices.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" style="text-align:center; padding: 2rem; color: var(--text-muted);">No enrolled devices found. Click "Enroll New Device" to add one.</td></tr>`;
    return;
  }

  devices.forEach(dev => {
    const tr = document.createElement('tr');
    const statusHtml = dev.isOnline
      ? `<span class="status-badge online"><span class="status-dot"></span> Online</span>`
      : `<span class="status-badge offline">○ Offline</span>`;

    const lastSeenFormatted = new Date(dev.lastSeen).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });

    tr.innerHTML = `
      <td>${statusHtml}</td>
      <td><strong>${escapeHtml(dev.deviceName)}</strong></td>
      <td><code>${escapeHtml(dev.deviceId.substring(0, 10))}...</code></td>
      <td><span title="${escapeHtml(dev.osVersion)}">${escapeHtml(dev.osVersion.length > 28 ? dev.osVersion.substring(0, 28) + '...' : dev.osVersion)}</span></td>
      <td>v${escapeHtml(dev.agentVersion)}</td>
      <td>${lastSeenFormatted}</td>
      <td class="text-right">
        <button class="btn-secondary btn-sm" onclick="inspectDevice('${dev.deviceId}')">Inspect</button>
        <button class="btn-secondary btn-sm" onclick="openTerminalForDevice('${dev.deviceId}')">Terminal</button>
        <button class="btn-danger-sm" onclick="uninstallDevice('${dev.deviceId}', '${escapeHtml(dev.deviceName)}')">Uninstall</button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function populateDeviceDropdowns() {
  const selects = [
    document.getElementById('inspector-device-select'),
    document.getElementById('terminal-device-select'),
    document.getElementById('files-device-select'),
    document.getElementById('rc-device-select')
  ].filter(Boolean);

  selects.forEach(select => {
    select.innerHTML = '<option value="">Select target computer...</option>';
    state.devices.forEach(d => {
      const opt = document.createElement('option');
      opt.value = d.deviceId;
      opt.textContent = `${d.deviceName} (${d.deviceId}) - ${d.isOnline ? 'Online' : 'Offline'}`;
      select.appendChild(opt);
    });
  });

  if (!state.selectedDeviceId && state.devices.length > 0) {
    state.selectedDeviceId = state.devices[0].deviceId;
    selects.forEach(s => s.value = state.selectedDeviceId);
    refreshDiagnostics();
  }
}

// Window actions for inline buttons
window.inspectDevice = function(deviceId) {
  state.selectedDeviceId = deviceId;
  document.getElementById('inspector-device-select').value = deviceId;
  switchTab('inspector');
  refreshDiagnostics();
};

window.openTerminalForDevice = function(deviceId) {
  state.selectedDeviceId = deviceId;
  document.getElementById('terminal-device-select').value = deviceId;
  switchTab('terminal');
  appendTerminalLine(`Switched target computer to [${deviceId}]`, 'text-cyan');
};

window.uninstallDevice = async function(deviceId, deviceName) {
  const confirmMsg = `Are you sure you want to completely uninstall "${deviceName || deviceId}"?\n\n` +
    `• If the target computer is online, the agent service and files will be cleanly removed remotely.\n` +
    `• This device will completely vanish from your dashboard.\n\n` +
    `Do you want to proceed?`;

  if (!confirm(confirmMsg)) return;

  try {
    const res = await fetch(`${API_BASE}/api/devices/${deviceId}/uninstall`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${state.jwtToken}`,
        'Content-Type': 'application/json'
      }
    });

    const data = await res.json().catch(() => ({}));
    if (res.ok) {
      alert(data.message || `Device "${deviceName || deviceId}" uninstalled successfully.`);
    } else {
      // Fallback: try DELETE /api/devices/:id
      const delRes = await fetch(`${API_BASE}/api/devices/${deviceId}`, {
        method: 'DELETE',
        headers: { 'Authorization': `Bearer ${state.jwtToken}` }
      });
      if (delRes.ok) {
        alert(`Device "${deviceName || deviceId}" removed from dashboard.`);
      } else {
        alert(data.message || 'Failed to uninstall device.');
      }
    }
  } catch (err) {
    console.error("Error uninstalling device:", err);
  } finally {
    state.devices = state.devices.filter(d => d.deviceId !== deviceId);
    if (state.selectedDeviceId === deviceId) {
      state.selectedDeviceId = state.devices[0]?.deviceId || null;
    }
    renderDevicesTable(state.devices);
    updateMetrics();
    populateDeviceDropdowns();
    await loadDevices();
  }
};

window.revokeDevice = window.uninstallDevice;

// Diagnostics & Specs
async function refreshDiagnostics() {
  const device = state.devices.find(d => d.deviceId === state.selectedDeviceId);
  if (!device) return;

  document.getElementById('inspector-device-subtitle').textContent =
    `Inspecting: ${device.deviceName} (${device.deviceId})`;

  const data = MOCK_DIAGNOSTICS; // Use specs

  document.getElementById('spec-computer-name').textContent = device.deviceName;
  document.getElementById('spec-cpu').textContent = data.cpuModel;
  document.getElementById('spec-ram').textContent = `${(data.totalMemoryBytes / (1024**3)).toFixed(1)} GB DDR5`;
  document.getElementById('spec-os').textContent = device.osVersion;
  document.getElementById('spec-arch').textContent = device.osArchitecture;
  document.getElementById('spec-agent').textContent = `v${device.agentVersion}`;

  // Storage
  const storageList = document.getElementById('storage-list-content');
  storageList.innerHTML = '';
  data.drives.forEach(drv => {
    const totalGb = (drv.totalBytes / (1024**3)).toFixed(1);
    const freeGb = (drv.availableBytes / (1024**3)).toFixed(1);
    const usedPercent = (((drv.totalBytes - drv.availableBytes) / drv.totalBytes) * 100).toFixed(0);

    const div = document.createElement('div');
    div.className = 'storage-item';
    div.innerHTML = `
      <div class="storage-labels">
        <span><strong>${drv.name}</strong> (${drv.volumeLabel} - ${drv.driveFormat})</span>
        <span class="text-secondary">${usedPercent}% used (${freeGb} GB free of ${totalGb} GB)</span>
      </div>
      <div class="progress-track">
        <div class="progress-fill ${usedPercent > 85 ? 'warning' : ''}" style="width: ${usedPercent}%;"></div>
      </div>
    `;
    storageList.appendChild(div);
  });

  // Networks
  const netList = document.getElementById('network-list-content');
  netList.innerHTML = '';
  data.network.forEach(net => {
    const div = document.createElement('div');
    div.style.marginBottom = '0.75rem';
    div.innerHTML = `
      <div><strong>${net.name}</strong> — <span class="text-green">${net.status}</span></div>
      <div style="font-size: 0.8rem; color: var(--text-muted);">${net.description}</div>
      <div style="font-size: 0.8rem; color: var(--accent-cyan); font-family: var(--font-mono);">${net.addresses.join(' | ')}</div>
    `;
    netList.appendChild(div);
  });

  // Processes
  const procTbody = document.getElementById('process-tbody');
  procTbody.innerHTML = '';
  data.processes.forEach(p => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><code>${p.processId}</code></td>
      <td><strong>${escapeHtml(p.name)}</strong></td>
      <td>${(p.workingSetBytes / (1024**2)).toFixed(1)} MB</td>
      <td>${new Date(p.startTime).toLocaleTimeString()}</td>
      <td class="text-secondary">${escapeHtml(p.description || '')}</td>
    `;
    procTbody.appendChild(tr);
  });
}

// Terminal Console Execution
async function handleTerminalCommand(e) {
  e.preventDefault();
  const input = document.getElementById('terminal-input');
  const cmd = input.value.trim();
  if (!cmd) return;

  input.value = '';
  const currentPrompt = state.currentDirectory ? `PS ${state.currentDirectory}> ` : 'PS> ';
  appendTerminalLine(`${currentPrompt}${cmd}`, 'text-primary');

  if (!state.jwtToken) {
    appendTerminalLine('Error: Not authenticated. Please sign in as Administrator first.', 'text-red');
    openAuthModal('Please sign in to execute remote terminal commands.');
    return;
  }

  if (!state.selectedDeviceId) {
    if (state.devices.length > 0) {
      state.selectedDeviceId = (state.devices.find(d => d.isOnline) || state.devices[0]).deviceId;
      populateDeviceDropdowns();
    } else {
      appendTerminalLine('Error: No target computer connected. Please enroll a device or ensure an agent is running.', 'text-red');
      return;
    }
  }

  // Handle local terminal helper commands
  if (cmd.toLowerCase() === 'help') {
    appendTerminalLine('Supported commands:\n  cd <path> (e.g. cd C:\\Users, cd ..)\n  pwd or Get-Location (prints current directory)\n  dir, mkdir <folder>, rmdir <folder>\n  open <url> or open <path> (e.g. open https://youtube.com)\n  All PowerShell commands execute statefully in the current working directory.', 'text-secondary');
    return;
  }

  if (cmd.toLowerCase() === 'clear' || cmd.toLowerCase() === 'cls') {
    document.getElementById('terminal-output').innerHTML = '';
    return;
  }

  appendTerminalLine(`Executing '${cmd}' in [${state.currentDirectory || 'C:\\'}]...`, 'text-muted');

  try {
    const res = await fetch(`${API_BASE}/api/devices/${state.selectedDeviceId}/exec`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${state.jwtToken}`
      },
      body: JSON.stringify({
        command: cmd,
        workingDirectory: state.currentDirectory || 'C:\\',
        timeoutSeconds: 45
      })
    });

    if (res.ok) {
      const result = await res.json();
      if (result.workingDirectory) {
        state.currentDirectory = result.workingDirectory;
        const promptLabel = document.getElementById('terminal-prompt-label');
        if (promptLabel) promptLabel.textContent = `PS ${result.workingDirectory}>`;
      }
      if (result.stdout) appendTerminalLine(result.stdout, 'text-green');
      if (result.stderr) {
        const cleanErr = formatTerminalOutput(result.stderr);
        if (cleanErr) appendTerminalLine(cleanErr, 'text-red');
      }
      if (!result.stdout && !result.stderr) appendTerminalLine('(Command completed with no output)', 'text-muted');
      appendTerminalLine(`[Process completed with exit code ${result.exitCode}]`, 'text-muted');
      return;
    } else {
      let errMsg = `HTTP ${res.status}`;
      try {
        const err = await res.json();
        errMsg = err.message || errMsg;
      } catch {}

      if (res.status === 401) {
        appendTerminalLine(`Error: Authentication expired (${errMsg}). Please sign in again.`, 'text-red');
        state.jwtToken = null;
        initAuthUI();
        openAuthModal('Session expired. Please sign in again.');
      } else {
        appendTerminalLine(`Remote Execution Error (${res.status}): ${errMsg}`, 'text-red');
      }
    }
  } catch (err) {
    appendTerminalLine(`Network Error: ${err.message}`, 'text-red');
  }
}

function formatTerminalOutput(text) {
  if (!text) return '';
  if (text.includes('#< CLIXML')) {
    const errorMatches = text.match(/<S S="Error">([\s\S]*?)<\/S>/g);
    if (errorMatches && errorMatches.length > 0) {
      return errorMatches
        .map(m => m.replace(/<S S="Error">/, '').replace(/<\/S>/, ''))
        .join('')
        .replace(/_x000D__x000A_/g, '\n')
        .replace(/_x001B_\[[0-9;]*m/g, '')
        .trim();
    }
  }
  return text;
}

function appendTerminalLine(text, className = '') {
  const screen = document.getElementById('terminal-output');
  const line = document.createElement('div');
  line.className = `terminal-line ${className}`;
  line.textContent = text;
  screen.appendChild(line);
  screen.scrollTop = screen.scrollHeight;
}

// Path Normalizer for Windows Paths
function normalizePath(rawPath) {
  if (!rawPath) return 'drives';
  let p = rawPath.trim();
  if (p === '/' || p === '\\' || p.toLowerCase() === 'drives') return 'drives';
  // Fix drive-relative path missing backslash (e.g. "C:Users" -> "C:\Users", "C:$Recycle.Bin" -> "C:\$Recycle.Bin")
  if (p.length >= 2 && p[1] === ':') {
    if (p.length === 2) {
      p += '\\';
    } else if (p[2] !== '\\' && p[2] !== '/') {
      p = p[0] + ':\\' + p.substring(2);
    }
  }
  return p;
}

// Live File Explorer
async function loadFiles() {
  const deviceId = state.selectedDeviceId;
  const pathInput = document.getElementById('files-current-path');
  const currentPath = normalizePath(pathInput.value);
  pathInput.value = currentPath;
  const tbody = document.getElementById('files-tbody');
  tbody.innerHTML = `<tr><td colspan="5" style="text-align:center; padding: 2.5rem; color: var(--text-muted);"><span class="loading-spinner"></span> Querying directory from remote device...</td></tr>`;

  if (!deviceId) {
    tbody.innerHTML = `<tr><td colspan="5" style="text-align:center; padding: 2rem; color: var(--accent-amber);">Please select an online target computer from the dropdown above.</td></tr>`;
    return;
  }

  if (!state.jwtToken) {
    tbody.innerHTML = `<tr><td colspan="5" style="text-align:center; padding: 2rem; color: var(--accent-amber);">Please sign in as Administrator to access files and drives.</td></tr>`;
    openAuthModal('Please sign in as Administrator to access remote files.');
    return;
  }

  try {
    const url = `${API_BASE}/api/devices/${deviceId}/files?path=${encodeURIComponent(currentPath)}`;
    const res = await fetch(url, {
      headers: { 'Authorization': `Bearer ${state.jwtToken}` }
    });

    if (res.ok) {
      const files = await res.json();
      tbody.innerHTML = '';
      if (!Array.isArray(files)) {
        const errMsg = files && files.message ? files.message : 'Invalid response from remote computer';
        tbody.innerHTML = `<tr><td colspan="5" style="text-align:center; padding: 2rem; color: var(--accent-red);">Failed to list directory: ${escapeHtml(errMsg)}</td></tr>`;
        return;
      }

      if (files.length === 0) {
        tbody.innerHTML = `<tr><td colspan="5" style="text-align:center; padding: 2rem; color: var(--text-muted);">Folder is empty.</td></tr>`;
        return;
      }

      // Sort folders first, then files alphabetically
      files.sort((a, b) => {
        if (a.isDirectory === b.isDirectory) return a.name.localeCompare(b.name);
        return a.isDirectory ? -1 : 1;
      });

      files.forEach(f => {
        const tr = document.createElement('tr');
        const sizeText = f.isDirectory ? '-' : (f.sizeBytes > 1024 * 1024 ? (f.sizeBytes / (1024 * 1024)).toFixed(1) + ' MB' : (f.sizeBytes / 1024).toFixed(1) + ' KB');
        const modifiedText = f.lastModified ? new Date(f.lastModified).toLocaleString() : '-';

        tr.innerHTML = `
          <td><strong>${f.isDirectory ? '📁' : '📄'} ${escapeHtml(f.name)}</strong></td>
          <td>${f.isDirectory ? 'Directory' : 'File'}</td>
          <td>${sizeText}</td>
          <td>${modifiedText}</td>
          <td class="text-right"></td>
        `;

        const actionTd = tr.querySelector('.text-right');
        if (f.isDirectory) {
          const openBtn = document.createElement('button');
          openBtn.className = 'btn-secondary btn-sm';
          openBtn.textContent = 'Open';
          openBtn.addEventListener('click', () => {
            enterDirectory(f.fullPath);
          });
          actionTd.appendChild(openBtn);
        } else {
          const dlBtn = document.createElement('button');
          dlBtn.className = 'btn-primary btn-sm';
          dlBtn.textContent = 'Download';
          dlBtn.addEventListener('click', () => {
            downloadFile(f.fullPath, f.name);
          });
          actionTd.appendChild(dlBtn);
        }

        tbody.appendChild(tr);
      });
      return;
    } else {
      let errMsg = 'Failed to load directory';
      try {
        const err = await res.json();
        errMsg = err.message || errMsg;
      } catch {}
      if (res.status === 401) {
        state.jwtToken = null;
        initAuthUI();
        openAuthModal('Session expired. Please sign in again.');
      }
      tbody.innerHTML = `<tr><td colspan="5" style="text-align:center; padding: 2rem; color: var(--accent-red);">Failed to list directory: ${escapeHtml(errMsg)}</td></tr>`;
    }
  } catch (err) {
    tbody.innerHTML = `<tr><td colspan="5" style="text-align:center; padding: 2rem; color: var(--accent-red);">Connection error: ${escapeHtml(err.message)}</td></tr>`;
  }
}

window.enterDirectory = function(path) {
  const norm = normalizePath(path);
  document.getElementById('files-current-path').value = norm;
  loadFiles();
};

window.navigateUp = function() {
  const input = document.getElementById('files-current-path');
  let current = normalizePath(input.value);
  if (!current || current.toLowerCase() === 'drives') return;

  current = current.replace(/[/\\]+$/, '');
  const lastSlash = Math.max(current.lastIndexOf('\\'), current.lastIndexOf('/'));
  if (lastSlash > 2) {
    input.value = current.substring(0, lastSlash);
  } else if (lastSlash === 2 && current.length > 3) {
    input.value = current.substring(0, 3); // e.g. C:\
  } else {
    input.value = 'drives'; // All drives
  }
  loadFiles();
};

window.downloadFile = async function(filePath, fileName) {
  const deviceId = state.selectedDeviceId;
  if (!deviceId) return;

  if (!state.jwtToken) {
    openAuthModal('Please sign in as Administrator to download files.');
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/api/devices/${deviceId}/files/download?path=${encodeURIComponent(filePath)}`, {
      headers: { 'Authorization': `Bearer ${state.jwtToken}` }
    });

    if (res.ok) {
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName || 'download';
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } else {
      let msg = 'Download failed';
      try {
        const err = await res.json();
        msg = err.message || msg;
      } catch {}
      if (res.status === 401) {
        state.jwtToken = null;
        initAuthUI();
        openAuthModal('Session expired. Please sign in again.');
      } else {
        alert(`Download failed: ${msg}`);
      }
    }
  } catch (err) {
    alert(`Download error: ${err.message}`);
  }
};

// Enrollment Code Generation
async function generateEnrollmentCode() {
  if (!state.jwtToken) {
    openAuthModal('Please sign in as Administrator to generate an enrollment code.');
    return;
  }

  try {
    let res = await fetch(`${API_BASE}/api/enrollment/code`, {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${state.jwtToken}` }
    });

    if (res.status === 401) {
      state.jwtToken = null;
      initAuthUI();
      openAuthModal('Session expired. Please sign in again.');
      return;
    }

    if (res.ok) {
      const data = await res.json();
      const code = data.code;
      document.getElementById('enroll-code-text').textContent = code;
      document.getElementById('code-result-box').style.display = 'block';

      const scriptUrl = `${API_BASE}/install.ps1`;
      const oneLiner = `irm ${scriptUrl} | iex; Install-LaptopCtl -EnrollmentCode "${code}" -Backend "${API_BASE}"`;
      document.getElementById('enroll-command-preview').textContent = oneLiner;
    } else {
      let errMsg = `Server returned status ${res.status}`;
      try {
        const err = await res.json();
        errMsg = err.message || errMsg;
      } catch {}
      alert(`Error generating code: ${errMsg}`);
    }
  } catch (err) {
    alert('Failed to connect to backend: ' + err.message);
  }
}

function copyEnrollmentCommand() {
  const text = document.getElementById('enroll-command-preview').textContent;
  navigator.clipboard.writeText(text).then(() => {
    const btn = document.getElementById('btn-copy-command');
    btn.innerHTML = `<span>Copied! ✓</span>`;
    setTimeout(() => {
      btn.innerHTML = `
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
          <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
        </svg>
        <span>Copy Command</span>`;
    }, 2000);
  });
}

// Audit Log
async function loadAuditLog() {
  const tbody = document.getElementById('audit-tbody');
  tbody.innerHTML = '';

  let events = MOCK_AUDIT;
  try {
    const res = await fetch(`${API_BASE}/api/audit`, {
      headers: { 'Authorization': `Bearer ${state.jwtToken}` }
    });
    if (res.ok) {
      events = await res.json();
    }
  } catch {}

  events.forEach(ev => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${new Date(ev.occurredAt).toLocaleString()}</td>
      <td><code>${escapeHtml(ev.eventType)}</code></td>
      <td>${ev.deviceId ? `<code>${escapeHtml(ev.deviceId)}</code>` : '<span class="text-muted">Global</span>'}</td>
      <td><span class="status-badge ${ev.success ? 'online' : 'offline'}">${ev.success ? 'SUCCESS' : 'DENIED'}</span></td>
      <td><code>${escapeHtml(ev.ipAddress || '127.0.0.1')}</code></td>
      <td>${escapeHtml(ev.description)}</td>
    `;
    tbody.appendChild(tr);
  });
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
}

// ─── Remote Control (Keyboard & Mouse) ─────────────────────────────────────────
async function executeRemoteCommand(deviceId, command) {
  if (!state.jwtToken) {
    openAuthModal('Please sign in as Administrator to send remote input.');
    throw new Error('Not authenticated.');
  }

  const targetId = deviceId || state.selectedDeviceId;
  if (!targetId) {
    throw new Error('No target computer selected. Please select a device from the dropdown.');
  }

  const res = await fetch(`${API_BASE}/api/devices/${targetId}/exec`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${state.jwtToken}`
    },
    body: JSON.stringify({
      command: command,
      workingDirectory: state.currentDirectory || 'C:\\',
      timeoutSeconds: 30
    })
  });

  if (!res.ok) {
    let errMsg = `HTTP ${res.status}`;
    try {
      const err = await res.json();
      errMsg = err.message || errMsg;
    } catch {}
    throw new Error(errMsg);
  }

  return await res.json();
}

function initRemoteControl() {
  const rcBanner = document.getElementById('rc-status-banner');
  const setBanner = (text, type = 'normal') => {
    if (!rcBanner) return;
    rcBanner.textContent = `[${new Date().toLocaleTimeString()}] ${text}`;
    rcBanner.className = `rc-status-banner ${type}`;
  };

  const runRcCommand = async (cmd, actionDesc) => {
    try {
      setBanner(`Executing: ${actionDesc}...`, 'normal');
      const result = await executeRemoteCommand(state.selectedDeviceId, cmd);
      const output = result.stdout || result.stderr || 'Action dispatched to active user session.';
      setBanner(`${actionDesc} completed:\n${output}`, result.exitCode === 0 ? 'success' : 'error');
    } catch (err) {
      setBanner(`Execution failed: ${err.message}`, 'error');
    }
  };

  // 1. Keyboard text typing in active window
  const btnTypeActive = document.getElementById('btn-rc-type-active');
  const keyboardText = document.getElementById('rc-keyboard-text');
  if (btnTypeActive && keyboardText) {
    btnTypeActive.addEventListener('click', () => {
      const text = keyboardText.value;
      if (!text) {
        setBanner('Please enter text to type into active window.', 'error');
        return;
      }
      runRcCommand(`type ${text}`, `Typing "${text.substring(0, 30)}${text.length > 30 ? '...' : ''}"`);
    });
  }

  // 2. Open Notepad & Type
  const btnTypeNotepad = document.getElementById('btn-rc-type-notepad');
  if (btnTypeNotepad && keyboardText) {
    btnTypeNotepad.addEventListener('click', () => {
      const text = keyboardText.value.trim() || 'Hello from LaptopCtl Remote Control!';
      const escaped = text.replace(/`/g, '``').replace(/"/g, '`"');
      const script = `Start-Process notepad; Start-Sleep -Seconds 2; Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait("${escaped}")`;
      runRcCommand(script, 'Open Notepad & Type Text');
    });
  }

  // Clear text
  const btnClearText = document.getElementById('btn-rc-clear-text');
  if (btnClearText && keyboardText) {
    btnClearText.addEventListener('click', () => {
      keyboardText.value = '';
    });
  }

  // 3. Quick keystrokes and hotkeys
  document.querySelectorAll('[data-hotkey]').forEach(btn => {
    btn.addEventListener('click', () => {
      const hotkey = btn.getAttribute('data-hotkey');
      if (hotkey) {
        runRcCommand(`hotkey ${hotkey}`, `Keystroke [${hotkey.toUpperCase()}]`);
      }
    });
  });

  // 4. Custom SendKeys sequence
  const btnSendCustom = document.getElementById('btn-rc-send-custom-keys');
  const customInput = document.getElementById('rc-custom-keys');
  if (btnSendCustom && customInput) {
    btnSendCustom.addEventListener('click', () => {
      const keys = customInput.value.trim();
      if (!keys) return;
      runRcCommand(`sendkeys ${keys}`, `Custom Key Sequence "${keys}"`);
    });
    customInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        btnSendCustom.click();
      }
    });
  }

  // 5. Mouse clicks
  const btnClickLeft = document.getElementById('btn-rc-click-left');
  if (btnClickLeft) {
    btnClickLeft.addEventListener('click', () => runRcCommand('click', 'Mouse Left Click'));
  }
  const btnClickRight = document.getElementById('btn-rc-click-right');
  if (btnClickRight) {
    btnClickRight.addEventListener('click', () => runRcCommand('rightclick', 'Mouse Right Click'));
  }
  const btnClickDouble = document.getElementById('btn-rc-click-double');
  if (btnClickDouble) {
    btnClickDouble.addEventListener('click', () => runRcCommand('doubleclick', 'Mouse Double Click'));
  }

  // 6. Coordinate click (X, Y)
  const btnClickCoords = document.getElementById('btn-rc-click-coords');
  const mouseX = document.getElementById('rc-mouse-x');
  const mouseY = document.getElementById('rc-mouse-y');
  if (btnClickCoords && mouseX && mouseY) {
    btnClickCoords.addEventListener('click', () => {
      const x = parseInt(mouseX.value) || 0;
      const y = parseInt(mouseY.value) || 0;
      runRcCommand(`click ${x} ${y}`, `Mouse Click at (${x}, ${y})`);
    });
  }

  // 7. On-screen popup alert
  const btnSendAlert = document.getElementById('btn-rc-send-alert');
  const alertText = document.getElementById('rc-alert-text');
  if (btnSendAlert && alertText) {
    btnSendAlert.addEventListener('click', () => {
      const msg = alertText.value.trim();
      if (!msg) {
        setBanner('Please enter an alert message to display.', 'error');
        return;
      }
      runRcCommand(`alert ${msg}`, `Screen Notification "${msg}"`);
    });
    alertText.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        btnSendAlert.click();
      }
    });
  }
}

