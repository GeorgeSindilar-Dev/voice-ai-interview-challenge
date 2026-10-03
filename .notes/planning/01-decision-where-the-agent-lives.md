# Decision: where does the agent's "brain" live?

A short presentation. Each `---` is one slide.

---

## 1. The question

Microsoft Foundry gives us two ways to build the voice agent:

| | Option 1: **Voice Live, tools handled in our backend** | Option 2: **Voice Live connected to a Foundry Agent** |
|---|---|---|
| Instructions (prompt) | In our code, sent when the session starts | Stored in the Foundry agent (portal or SDK) |
| Tools | Defined in our code; when the model calls one, the request arrives in **our** backend | Defined on the Foundry agent; Foundry calls them (for example our HTTP API through an OpenAPI tool) |
| Who runs the tool | Our backend | Foundry Agent Service |

**Both options use Foundry's built-in functionality.** Voice Live (speech
recognition, the LLM, the voice, interruption handling, noise suppression, content
filtering) is the same service in both. Function calling is also a built-in Voice
Live feature. The only difference is **where the prompt and tools are defined, and
who executes a tool call.**

---

## 2. What is the same in both options

Whichever option we pick, we still build:

- the telephony bridge (ACS phone call ↔ our backend ↔ Voice Live)
- the browser voice page (browser microphone ↔ our backend ↔ Voice Live)
- the backend state machine that talks to the mock issuer and ticket services
- the mock services, the reset form, reconciliation, tests and docs

Option 2 does **not** remove the hard parts. It moves the prompt and the tool
wiring into Foundry.

---

## 3. How option 2 works

```
Caller ──audio──► our backend ──audio──► Voice Live ──► Foundry Agent
                                                          │
                                       tool call (HTTP)   ▼
                              our tools API ◄──────── Foundry Agent Service
```

When the model wants to verify a code, **Foundry** calls our tools API. That
request comes from Foundry, not from the phone call. So our API must answer:
**which call is this for?**

The only information in the request is what the **model** put in the tool
arguments. If the model has to pass a session or recovery ID, the model now
chooses which recovery it acts on. That is exactly what the challenge forbids:

> "never use a caller-supplied ID to attach another session's verification"
> (mock contract) and "The backend, not the model, must enforce state
> transitions and bind operations to the correct account and recovery session"
> (README).

A caller could try "use recovery r-123", or a prompt injection could make the
model pass another session's ID. We could defend against this (per-session
credentials for every tool call), but that is extra security work just to get
back to where option 1 starts.

---

## 4. How option 1 works

```
Caller ──audio──► our backend ◄══ one WebSocket per call ══► Voice Live
                      │
                      │ tool call event arrives on THIS call's WebSocket
                      ▼
              state machine for THIS call ──► mock issuer / ticket APIs
```

Our backend opens **one Voice Live connection per call**. When the model calls a
tool, the event arrives on that call's own connection. The backend already knows
which call, account and recovery it belongs to. **The model never sees or chooses
a recovery ID.** Tools such as `submit_code(code)` take only what the caller said.

The state machine then decides: "Is a code submission allowed right now, for this
call?" If not, it refuses, whatever the model says.

---

## 5. Further benefits of option 1

1. **Everything is versioned in git.** The challenge says the deployment must match
   the pinned commit SHA. With option 2, the prompt and tools live in the Foundry
   portal and can drift from the code. With option 1, the prompt, tools and
   guardrails are in the repository at that exact commit, so reviewers can read them.
2. **Testable.** Tools are plain backend code. We can unit-test "a third code
   submission is refused" without any AI or phone involved.
3. **Lower latency.** There's one network hop fewer per tool call. That matters
   because the code expires in 120 seconds and the challenge measures latency.
4. **One place for state.** Restart recovery, reconciliation and duplicate handling
   all live in our backend. With option 2 there is also conversation state inside
   Foundry to reason about.
5. **The same code for phone and browser.** Both channels are just audio sources
   feeding the same backend session.
6. **Simpler authentication.** Agent mode supports only Microsoft Entra ID for
   invoking the agent. Model mode is a single connection that our backend controls.
7. **Easier to explain in the video.** The trust boundary is one line: "the model
   proposes, our backend decides." That's 40% of the score.

---

## 6. What we give up with option 1

| Option 2 gives us | Our replacement in option 1 |
|---|---|
| Portal playground for testing the prompt | Our browser voice page plus a text-mode test harness |
| Edit the prompt without redeploying | Redeploy (and a redeploy is what keeps the commit SHA honest) |
| Built-in agent tracing and evaluations | Application Insights traces plus our own scenario tests |
| Managed conversation threads | Not needed: each call is short and the backend owns the state |

Content filtering is part of Voice Live, so we keep it in both options.

---

## 7. When option 2 would be the better choice

- Tools are **stateless lookups** (knowledge base, FAQ, order status by an ID the
  user is allowed to see), so the "which session?" question doesn't matter.
- The same agent is shared across many channels and products.
- Non-developers need to change the prompt often.

None of these apply to this challenge, where every tool call changes security
state for one specific call.

---

## 8. Recommendation

**Option 1: Voice Live in model mode, with tools executed by our backend.**

- We still use Foundry's built-in voice functionality (Voice Live), not a
  hand-built speech pipeline.
- Our backend owns authorization, which is the main thing the challenge assesses.
- Everything that defines the agent's behaviour is in git and testable.

Caveat: Microsoft's docs don't clearly say which tool types work in Voice Live
agent mode. That uncertainty is one more reason to choose the option whose
behaviour we fully control.
