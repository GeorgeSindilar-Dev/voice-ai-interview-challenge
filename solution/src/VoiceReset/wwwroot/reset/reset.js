// Reset form. The token comes from the URL fragment and stays in this module only.
// Rules: no console output at all; every failure is shown on the page; server text only via textContent.
const EXPIRED = 'This link has expired. Call the assistant again to start a new request.';
const GENERIC = 'Something went wrong. Please try again in a moment.';

// Server error code -> fixed text. Codes in FINAL end the attempt (the link is unusable).
const MESSAGES = {
  invalid_token: 'This link is not valid. Open the link from your recovery inbox.',
  token_used: 'This link has already been used. If you just submitted a new password, it may already be changed — tell the assistant.',
  link_expired: EXPIRED,
  throttled: 'Too many attempts. Wait a moment and try again.',
};
const UNCONFIRMED = 'We could not confirm the change. Please tell the assistant on the call.';
const PENDING = 'Your change is being processed. Return to the call and ask the assistant to check it.';
const SUCCESS = 'Your password has been changed. Return to the call, then sign in with it.';
const FINAL = new Set(['invalid_token', 'token_used', 'link_expired']);

const messageEl = document.getElementById('reset-message');
const formEl = document.getElementById('reset-form');
const rulesEl = document.getElementById('policy-rules');
const passwordEl = document.getElementById('new-password');
const confirmEl = document.getElementById('confirm-password');
const submitEl = document.getElementById('submit-button');

let token = takeTokenFromAddressBar();

function takeTokenFromAddressBar() {
  const value = new URLSearchParams(location.hash.slice(1)).get('token');
  if (location.hash) {
    history.replaceState(null, '', location.pathname); // the token never stays in the address bar
  }
  return value || null;
}

function showMessage(text, kind, details = []) {
  messageEl.replaceChildren();
  messageEl.dataset.kind = kind;
  const paragraph = document.createElement('p');
  paragraph.textContent = text;
  messageEl.append(paragraph);
  if (details.length > 0) {
    const list = document.createElement('ul');
    for (const detail of details) {
      const item = document.createElement('li');
      item.textContent = detail;
      list.append(item);
    }
    messageEl.append(list);
  }
}

// Ends the attempt: the token is dropped and the form is hidden.
function finish(text, kind) {
  token = null;
  formEl.hidden = true;
  showMessage(text, kind);
}

async function postJson(url, body) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    credentials: 'omit',
    cache: 'no-store',
  });
  let data = null;
  try {
    data = await response.json();
  } catch {
    // an empty or non-JSON body is handled as a generic failure
  }
  return { ok: response.ok, data };
}

function showSuccess() {
  finish(SUCCESS, 'success');
  document.getElementById('login-link').hidden = false;
}

// After a lost answer: ask the issuer what happened to this submission. The token can only read its own operation.
async function checkOperation(operationId) {
  try {
    const response = await fetch(`/mock/v1/reset-operations/${encodeURIComponent(operationId)}`, {
      headers: { Authorization: `ResetToken ${token}` },
      credentials: 'omit',
      cache: 'no-store',
    });
    const data = response.ok ? await response.json() : null;
    if (data?.status === 'succeeded') {
      showSuccess();
      return;
    }
  } catch {
    // still unknown
  }
  showMessage(UNCONFIRMED, 'error');
}

async function loadPolicy() {
  let rules = ['The rules could not be loaded. The server still checks your password.'];
  try {
    const response = await fetch('/mock/v1/policy', { credentials: 'omit', cache: 'no-store' });
    if (response.ok) {
      rules = (await response.json()).rules.map((rule) => String(rule.description));
    }
  } catch {
    // keep the fallback text
  }
  for (const rule of rules) {
    const item = document.createElement('li');
    item.textContent = rule;
    rulesEl.append(item);
  }
}

function showViolations(violations) {
  const reasons = Array.isArray(violations) ? violations.map((v) => String(v.description)) : [];
  showMessage('That password does not meet the rules.', 'error', reasons);
  passwordEl.focus();
}

function handleFailure(result) {
  const code = result.data?.error?.code;
  if (code === 'policy_violation') {
    showViolations(result.data.violations);
  } else if (FINAL.has(code)) {
    finish(MESSAGES[code], 'error');
  } else {
    showMessage(MESSAGES[code] ?? GENERIC, 'error');
    passwordEl.focus();
  }
}

async function onSubmit(event) {
  event.preventDefault();
  const password = passwordEl.value;
  if (password !== confirmEl.value) {
    showMessage('The two passwords do not match.', 'error');
    confirmEl.focus();
    return;
  }

  passwordEl.value = '';
  confirmEl.value = '';
  submitEl.disabled = true;
  const operationId = crypto.randomUUID(); // a new operation for every submitted password
  let resetSent = false;
  showMessage('Checking your password...', 'info');
  try {
    const checked = await postJson('/mock/v1/password/validate', { token, password });
    if (!checked.ok) {
      handleFailure(checked);
    } else if (!checked.data?.valid) {
      showViolations(checked.data?.violations);
    } else {
      resetSent = true;
      const reset = await postJson('/mock/v1/resets', { token, new_password: password, operation_id: operationId });
      if (reset.ok && reset.data?.status === 'succeeded') {
        showSuccess();
      } else if (reset.ok && reset.data?.status === 'pending') {
        finish(PENDING, 'info');
      } else if (reset.ok) {
        finish('The password could not be changed right now. Tell the assistant on the call.', 'error');
      } else {
        handleFailure(reset);
      }
    }
  } catch {
    if (resetSent) {
      await checkOperation(operationId); // the password may already have changed
    } else {
      showMessage(GENERIC, 'error');
      passwordEl.focus();
    }
  } finally {
    submitEl.disabled = false;
  }
}

// A used, expired or unknown link is reported before the caller types a password. The token is checked
// before the password, so an empty password is enough; if the check fails, the server still checks on submit.
async function start() {
  try {
    const checked = await postJson('/mock/v1/password/validate', { token, password: '' });
    const code = checked.data?.error?.code;
    if (FINAL.has(code)) {
      finish(MESSAGES[code], 'error');
      return;
    }
  } catch {
    // show the form anyway
  }
  formEl.hidden = false;
  formEl.addEventListener('submit', onSubmit);
  loadPolicy();
}

if (token) {
  start();
} else {
  showMessage('Open the link from your recovery inbox.', 'info');
}
