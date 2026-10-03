# T13 Reset Form Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A small static page at `/reset/` where the caller types the new password. It reads the token from the URL fragment, validates the password, and completes the reset against the same-origin mock API.

**Architecture:** Static HTML + one vanilla JS module, no inline script/style (CSP-safe). The token is read once from `location.hash`, kept in a module variable and removed from the address bar. The browser calls `GET /mock/v1/policy`, `POST /mock/v1/password/validate`, `POST /mock/v1/resets` (browser routes, no service auth). Server text goes in with `textContent` only.

**Tech Stack:** HTML5, ES modules, `fetch`, `crypto.randomUUID()`, ASP.NET Core static files, xUnit v3 + `WebApplicationFactory<Program>`.

**Depends on:** T1. Mock routes (T4) are needed only for the manual check. The response headers for `/reset/*` (`Referrer-Policy: no-referrer`, `Cache-Control: no-store`) come from the T14 security-headers middleware, not from this task.

**Files:**
- Create: `solution/src/VoiceReset/wwwroot/reset/index.html`, `solution/src/VoiceReset/wwwroot/reset/reset.js`
- Modify: `solution/src/VoiceReset/wwwroot/css/site.css` (append one section; create the file if T12 has not yet)
- Modify: `solution/src/VoiceReset/Program.cs` (only if `UseDefaultFiles()` / `UseStaticFiles()` are missing)
- Test: `solution/tests/VoiceReset.Tests/Reset/ResetPageTests.cs`

Element ids shared by the test, the HTML and `reset.js`: `reset-message`, `reset-form`, `policy-rules`, `new-password`, `confirm-password`, `submit-button`.

---

### Task 1: Failing test, static-file wiring, HTML

- [ ] **Step 1: Write `ResetPageTests.cs`** (no inline script or style may be used on the page; the T14 content security policy blocks both)

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceReset.Tests.Reset;

public sealed class ResetPageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] s_ids =
        ["reset-message", "reset-form", "policy-rules", "new-password", "confirm-password", "submit-button"];

    [Fact]
    public async Task GetResetPage_Always_ServesFormWithAllElements()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/reset/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var script = await client.GetAsync("/reset/reset.js", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(s_ids, id => Assert.Contains($"id=\"{id}\"", html));
        Assert.Contains("autocomplete=\"new-password\"", html);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }
}
```

- [ ] **Step 2: Run, expect FAIL (404)**

Run (from `solution/`): `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Reset.ResetPageTests"`
Expected: the test fails.

- [ ] **Step 3: Serve static files.** In `Program.cs`, before the endpoint mappings (and after T14's `UseSecurityHeaders()` when present), make sure these exist:

```csharp
app.UseDefaultFiles();   // /reset/ -> /reset/index.html
app.UseStaticFiles();
```

- [ ] **Step 4: Create `wwwroot/reset/index.html`** (the form is `hidden` until JS finds a token; `reset-message` is in the DOM from the start so screen readers announce changes)

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <title>Set a new password</title>
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="/css/site.css">
</head>
<body class="reset-page">
  <main class="reset-card">
    <h1>Set a new password</h1>
    <div id="reset-message" class="reset-message" role="status" aria-live="polite"></div>

    <form id="reset-form" class="reset-form" hidden>
      <h2 id="policy-title">Password rules</h2>
      <ul id="policy-rules" aria-labelledby="policy-title"></ul>

      <label for="new-password">New password</label>
      <input id="new-password" name="new-password" type="password"
             autocomplete="new-password" autocapitalize="off" spellcheck="false" required>

      <label for="confirm-password">Confirm new password</label>
      <input id="confirm-password" name="confirm-password" type="password"
             autocomplete="new-password" autocapitalize="off" spellcheck="false" required>

      <button id="submit-button" type="submit">Change password</button>
    </form>

    <p class="reset-note">Never tell your password to the assistant on the call. Type it only on this page.</p>
  </main>
  <script type="module" src="/reset/reset.js"></script>
</body>
</html>
```

- [ ] **Step 5:** create `wwwroot/reset/reset.js` containing only `// reset form`, rerun the tests. Expected: PASS.

---

### Task 2: Script and styles

- [ ] **Step 1: Replace `reset.js`**

```js
// Reset form. The token comes from the URL fragment and stays in this module only.
const EXPIRED = 'This link has expired. Call the assistant again to start a new request.';
const GENERIC = 'Something went wrong. Please try again in a moment.';

// Server error code -> fixed text. Codes in FINAL end the attempt (the link is unusable).
const MESSAGES = {
  invalid_token: 'This link is not valid. Open the link from your recovery inbox.',
  token_used: 'This link has already been used.',
  link_expired: EXPIRED,
  recovery_expired: EXPIRED,
  throttled: 'Too many attempts. Wait a moment and try again.',
};
const FINAL = new Set(['invalid_token', 'token_used', 'link_expired', 'recovery_expired']);

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
  }
}

function handleReset(data) {
  if (data?.status === 'succeeded') {
    finish('Your password has been changed. You can close this page and return to the call.', 'success');
  } else if (data?.status === 'pending') {
    finish('Your request was accepted and is being completed. You can close this page and return to the call.', 'info');
  } else {
    finish('The password could not be changed right now. Tell the assistant on the call.', 'error');
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
  showMessage('Checking your password...', 'info');
  try {
    const checked = await postJson('/mock/v1/password/validate', { token, password });
    if (!checked.ok) {
      handleFailure(checked);
    } else if (!checked.data?.valid) {
      showViolations(checked.data?.violations);
    } else {
      const reset = await postJson('/mock/v1/resets', {
        token,
        new_password: password,
        operation_id: crypto.randomUUID(), // a new operation for every attempt
      });
      if (reset.ok) {
        handleReset(reset.data);
      } else {
        handleFailure(reset);
      }
    }
  } catch {
    showMessage(GENERIC, 'error');
  } finally {
    submitEl.disabled = false;
  }
}

if (token) {
  formEl.hidden = false;
  formEl.addEventListener('submit', onSubmit);
  loadPolicy();
} else {
  showMessage('Open the link from your recovery inbox.', 'info');
}
```

Keep: no `console.*`/`innerHTML`; inputs are cleared before the first `await`; the token is never copied into the DOM or storage.

- [ ] **Step 2: Append to `wwwroot/css/site.css`** (all selectors scoped to the page, so the agent page is unaffected)

```css
/* ---- Reset form (/reset/) ---- */
.reset-page { margin: 0; min-height: 100vh; display: grid; place-items: start center; padding: 1.5rem 1rem;
  font-family: system-ui, sans-serif; background: #f4f6f8; color: #1b1f24; }
.reset-card { width: 100%; max-width: 28rem; background: #fff; border-radius: 0.5rem; padding: 1.5rem;
  box-shadow: 0 1px 4px rgb(0 0 0 / 15%); }
.reset-card h1 { margin: 0 0 1rem; font-size: 1.4rem; }
.reset-form { display: grid; gap: 0.5rem; }
.reset-form[hidden], .reset-message:empty { display: none; }
.reset-form ul, .reset-message ul { margin: 0.25rem 0 0.75rem; padding-left: 1.25rem; }
.reset-form label { font-weight: 600; }
.reset-form input { font: inherit; padding: 0.6rem; border: 1px solid #6b7380; border-radius: 0.3rem; }
.reset-form button { font: inherit; margin-top: 0.75rem; padding: 0.7rem; border: 0; border-radius: 0.3rem;
  background: #1f5fbf; color: #fff; cursor: pointer; }
.reset-form button:disabled { background: #7d8da6; cursor: wait; }
.reset-page :focus-visible { outline: 3px solid #f2a900; outline-offset: 2px; }
.reset-message { margin-bottom: 1rem; padding: 0.75rem; border-radius: 0.3rem; background: #e8f0fb; }
.reset-message p { margin: 0; }
.reset-message[data-kind="error"] { background: #fde8e8; color: #7a1212; }
.reset-message[data-kind="success"] { background: #e3f6e8; color: #145a2a; }
.reset-note { margin: 1rem 0 0; font-size: 0.9rem; color: #4a5260; }
```

- [ ] **Step 3: Test, build, commit**

Run (from `solution/`): `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Reset.ResetPageTests"` (expect PASS) and `dotnet build VoiceReset.slnx -c Release` (expect `0 Warning(s)`).

```bash
git add solution/src/VoiceReset solution/tests/VoiceReset.Tests/Reset
git commit -m "feat: add password reset form page"
```

---

### Task 3: Manual check (browser or Playwright MCP by hand; no test code is committed)

Needs T3, T4, T5 and T14 merged; app running (`dotnet run --project src/VoiceReset`, Development settings).

- [ ] **Step 1: Get a real link.** With a REST client and the dev service credential from `appsettings.Development.json`: `POST /mock/v1/recoveries` (`username`, `request_id`), read the code at `/mock/inbox` (dev inbox login), `POST .../verify` (with `Idempotency-Key`), `POST .../reset-link` (`operation_id`), then open the link shown in the inbox.
- [ ] **Step 2: Check each case**

| Case | Expected |
|---|---|
| `/reset/` without fragment | "Open the link from your recovery inbox."; no form |
| The real link | Address bar becomes `/reset/` (fragment gone); form and rules shown; no `Referer` header on the API calls |
| Different passwords / a password that breaks a rule | Mismatch text, no request / rule descriptions listed, both fields empty, form still usable |
| Valid password | "Your password has been changed..."; form hidden; one `validate` and one `resets` request |
| Reload after success; open the used link again | First: "Open the link..."; second: "This link has already been used." |
| `/reset/` response headers | `Referrer-Policy: no-referrer`, `Cache-Control: no-store` |
| 375 px width, keyboard only | Fits; Tab order password, confirm, button; focus ring visible |

- [ ] **Step 3: Console.** No output from our code on the success path. The browser's own "Failed to load resource" lines for expected 4xx answers cannot be suppressed and are not `console.*` calls. `grep -n "console\.\|innerHTML\|localStorage\|sessionStorage" solution/src/VoiceReset/wwwroot/reset/reset.js` returns nothing.

## Questions

1. Does T4 answer `200 {status:"succeeded"}` or sometimes `202 pending`? For `pending` the page only says "being completed"; it does not poll `GET /v1/reset-operations/{id}` (not in the shared contracts). OK?
2. If the reset response is lost after the server accepted it, the next attempt gets `token_used` and the page says "already used", which could hide a success. Kept simple; the agent reconciles via recovery status. OK?

## Additions to contracts

- Element ids of `/reset/`: `reset-message`, `reset-form`, `policy-rules`, `new-password`, `confirm-password`, `submit-button`.
- `Program.cs` calls `app.UseDefaultFiles()` then `app.UseStaticFiles()` (T12 or T13, whoever lands first); T14 `UseSecurityHeaders()` must come before both.
- `site.css` rules are scoped per page (`.reset-*` here); T12 should scope its own the same way.
