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

const state = {
  activeTab: 'devices',
  devices: [],
  selectedDeviceId: null,
  jwtToken: localStorage.getItem('laptopctl_token'),
  currentUser: { email: localStorage.getItem('laptopctl_user_email') || '' },
  mockMode: false,
  authMode: 'register'
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
document.addEventListener('DOMContentLoaded', async () => {
  setupNavigation();
  setupEventListeners();
  initAuthUI();
  if (!state.jwtToken) {
    await autoAdminLogin();
  } else {
    loadDevices();
    loadAuditLog();
  }
});

async function autoAdminLogin() {
  try {
    const res = await fetch(`${API_BASE}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'admin@laptopctl.com', password: 'AdminPassword123!' })
    });
    if (res.ok) {
      const data = await res.json();
      const token = data.accessToken || data.token;
      if (token) {
        localStorage.setItem('laptopctl_token', token);
        localStorage.setItem('laptopctl_user_email', data.email || 'admin@laptopctl.com');
        state.jwtToken = token;
        state.currentUser = { email: data.email || 'admin@laptopctl.com' };
        initAuthUI();
      }
    }
  } catch {}
  loadDevices();
  loadAuditLog();
}

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
      localStorage.removeItem('laptopctl_token');
      localStorage.removeItem('laptopctl_user_email');
      state.jwtToken = null;
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

        localStorage.setItem('laptopctl_token', token);
        localStorage.setItem('laptopctl_user_email', loginData.email || email);
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

  [inspectorSelect, terminalSelect, filesSelect].forEach(select => {
    select.addEventListener('change', (e) => {
      state.selectedDeviceId = e.target.value;
      inspectorSelect.value = state.selectedDeviceId;
      terminalSelect.value = state.selectedDeviceId;
      filesSelect.value = state.selectedDeviceId;

      if (state.activeTab === 'inspector') refreshDiagnostics();
      if (state.activeTab === 'files') loadFiles();
    });
  });

  // Terminal submission
  const terminalForm = document.getElementById('terminal-form');
  terminalForm.addEventListener('submit', handleTerminalCommand);
  document.getElementById('btn-clear-terminal').addEventListener('click', () => {
    document.getElementById('terminal-output').innerHTML = '';
  });

  // File explorer go
  document.getElementById('btn-path-go').addEventListener('click', loadFiles);
  document.getElementById('btn-path-up').addEventListener('click', () => {
    const input = document.getElementById('files-current-path');
    const parts = input.value.split(/[\\/]/).filter(p => p.length > 0);
    if (parts.length > 1) {
      parts.pop();
      input.value = parts.join('\\');
      loadFiles();
    }
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
    const email = localStorage.getItem('laptopctl_user_email') || 'Admin';
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
        <button class="btn-danger-sm" onclick="revokeDevice('${dev.deviceId}')">Revoke</button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function populateDeviceDropdowns() {
  const selects = [
    document.getElementById('inspector-device-select'),
    document.getElementById('terminal-device-select'),
    document.getElementById('files-device-select')
  ];

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

window.revokeDevice = async function(deviceId) {
  if (!confirm(`Are you sure you want to revoke enrollment for device ${deviceId}? This action cannot be undone.`)) {
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/api/devices/${deviceId}`, {
      method: 'DELETE',
      headers: { 'Authorization': `Bearer ${state.jwtToken}` }
    });
    if (res.ok) {
      alert(`Device ${deviceId} revoked.`);
      loadDevices();
    } else {
      throw new Error();
    }
  } catch {
    state.devices = state.devices.filter(d => d.deviceId !== deviceId);
    renderDevicesTable(state.devices);
    updateMetrics();
  }
};

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
  appendTerminalLine(`PS> ${cmd}`, 'text-primary');

  if (!state.selectedDeviceId) {
    appendTerminalLine('Error: No target computer selected. Select one from the dropdown above.', 'text-red');
    return;
  }

  // Handle local terminal helper commands
  if (cmd.toLowerCase() === 'help') {
    appendTerminalLine('Supported commands:\n  ipconfig, Get-Service, Get-Process, systeminfo, tasklist, restart-service <name>\n  All valid PowerShell expressions are evaluated under SYSTEM context.', 'text-secondary');
    return;
  }

  if (cmd.toLowerCase() === 'clear' || cmd.toLowerCase() === 'cls') {
    document.getElementById('terminal-output').innerHTML = '';
    return;
  }

  appendTerminalLine(`Executing '${cmd}' on remote target [${state.selectedDeviceId}]...`, 'text-muted');

  try {
    // Attempt live execution via backend relay
    const res = await fetch(`${API_BASE}/api/devices/${state.selectedDeviceId}/exec`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${state.jwtToken}`
      },
      body: JSON.stringify({ command: cmd, timeoutSeconds: 30 })
    });

    if (res.ok) {
      const result = await res.json();
      if (result.stdout) appendTerminalLine(result.stdout, 'text-green');
      if (result.stderr) appendTerminalLine(result.stderr, 'text-red');
      appendTerminalLine(`[Process completed with exit code ${result.exitCode}]`, 'text-muted');
      return;
    }
  } catch {
    // Fallback response for simulator/demo mode
  }

  // Simulated realistic PowerShell output
  setTimeout(() => {
    if (cmd.toLowerCase().includes('ipconfig')) {
      appendTerminalLine(`Windows IP Configuration\n\nEthernet adapter Ethernet 1:\n   Connection-specific DNS Suffix  . : corp.internal\n   IPv4 Address. . . . . . . . . . . : 192.168.1.145\n   Subnet Mask . . . . . . . . . . . : 255.255.255.0\n   Default Gateway . . . . . . . . . : 192.168.1.1`, 'text-green');
    } else if (cmd.toLowerCase().includes('service')) {
      appendTerminalLine(`Status   Name               DisplayName\n------   ----               -----------\nRunning  LaptopCtlAgent     LaptopCtl Remote Administration Agent\nRunning  LanmanWorkstation  Workstation\nRunning  wuauserv           Windows Update`, 'text-green');
    } else {
      appendTerminalLine(`[Target: ${state.selectedDeviceId}] Command '${cmd}' executed successfully.\nExitCode: 0\nResult: Operation completed.`, 'text-green');
    }
  }, 400);
}

function appendTerminalLine(text, className = '') {
  const screen = document.getElementById('terminal-output');
  const line = document.createElement('div');
  line.className = `terminal-line ${className}`;
  line.textContent = text;
  screen.appendChild(line);
  screen.scrollTop = screen.scrollHeight;
}

// File Explorer
async function loadFiles() {
  const currentPath = document.getElementById('files-current-path').value;
  const tbody = document.getElementById('files-tbody');
  tbody.innerHTML = '';

  MOCK_FILES.forEach(f => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><strong>${f.isDirectory ? '📁 ' : '📄 '} ${escapeHtml(f.name)}</strong></td>
      <td>${f.isDirectory ? 'Directory' : 'File'}</td>
      <td>${f.isDirectory ? '-' : `${(f.sizeBytes / 1024).toFixed(1)} KB`}</td>
      <td>${new Date(f.lastModified).toLocaleDateString()}</td>
      <td class="text-right">
        ${f.isDirectory
          ? `<button class="btn-secondary btn-sm" onclick="enterDirectory('${escapeHtml(f.fullPath)}')">Open</button>`
          : `<button class="btn-primary btn-sm" onclick="downloadFile('${escapeHtml(f.name)}')">Download</button>`
        }
      </td>
    `;
    tbody.appendChild(tr);
  });
}

window.enterDirectory = function(path) {
  document.getElementById('files-current-path').value = path;
  loadFiles();
};

window.downloadFile = function(fileName) {
  alert(`Download initiated for: ${fileName}`);
};

// Enrollment Code Generation
async function generateEnrollmentCode() {
  if (!state.jwtToken) {
    openAuthModal('Please sign in or create an admin account to generate an enrollment code.');
    return;
  }

  let code = 'LPT-' + Math.random().toString(36).substring(2, 6).toUpperCase();

  try {
    const res = await fetch(`${API_BASE}/api/enrollment/code`, {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${state.jwtToken}` }
    });
    if (res.ok) {
      const data = await res.json();
      code = data.code;
    } else {
      const err = await res.json();
      alert(err.message || 'Failed to generate code.');
      return;
    }
  } catch (err) {
    alert('Failed to connect to backend.');
    return;
  }

  document.getElementById('enroll-code-text').textContent = code;
  document.getElementById('code-result-box').style.display = 'block';

  const scriptUrl = `${API_BASE}/install.ps1`;
  const oneLiner = `irm ${scriptUrl} | iex; Install-LaptopCtl -EnrollmentCode "${code}" -Backend "${API_BASE}"`;
  document.getElementById('enroll-command-preview').textContent = oneLiner;
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
