# Tester feedback (2026-10-04)

A friend tested the deployed agent in the browser (calls on 2026-10-03, about 17:56–18:05 UTC).
Summary of what they reported and what was done (commit bdfc66a).

**Worked well:** the full flow (old password refused after the reset, new one accepted); no account
enumeration; no call without the access code; single-use link; token removed from the address bar;
never asks for the password; ends the call on "goodbye".

| # | Report | Finding | Done |
|---|---|---|---|
| 1 | The first letter of the username is lost ("A L E X" → "L E X", "V O I C A" → "O I C A") | Their guess (the `clear` message) is wrong: `clear` only drops the agent's unplayed audio in the browser. The transcripts show speech-to-text merging spelled letters into words ("LEX", "OICA", "RACDAN") and losing the first one. | Prompt asks for the username as words ("alex dot morgan") and spelling only of unclear parts; `PrefixPadding` 600 ms; `RemoveFillerWords` off (a lone "A" can look like "uh"); browser noise suppression off (Voice Live already does it). **Check in the next live test.** |
| 2 | Letters that sound alike (Z/C, V/D) | | Prompt: accept "V as in Victor" and spelling words, ask about look-alike letters, read back with a word per confusable letter. |
| 3 | The spoken greeting doesn't say it is AI | | Greeting and prompt say "automated AI assistant". |
| 4 | 1.3–3 s latency, longer on code and reset checks | | Prompt: "One moment." before slow tools. Not done: fewer blob writes per tool (could save 0.3–0.8 s), only if still slow. |
| 5a | Two calls at once on the same access code | The README exercises concurrent sessions. | No change; document. |
| 5b | A used link shows the form; the error comes after typing the password | | The form checks the link on load (`validate` with an empty password; the token is checked first). |
| 5c | With the mic muted, the call drops after about 5 s (unconfirmed) | No such drop in the logs; the dropped calls all had the `session.update` error (now fixed). | Covered by the silence handling; check in the next test. |

**Before handing in:**
- One call on laptop speakers without headphones (echo check). Manual, by the owner.
- Reset `alex.morgan` to its initial password: remove its entry from `PasswordHashes` in blob
  `state/mock/state.json` (deleting the whole blob resets all mock state). Do it last.

## Probe: is the first word lost by Voice Live or by the browser? (2026-10-04)

A local probe sent recorded speech (Windows TTS, 24 kHz PCM, real-time pace, 1.5 s silence first)
straight to Voice Live with our settings and read the transcription:

| Settings | "Alex dot Morgan" | "A. L. E. X." | "V. O. I. C. A. dot Razvan" |
|---|---|---|---|
| current (600 ms padding, echo cancellation, noise suppression) | alex.morgan | ALEX | VOICA.rasvin |
| without echo cancellation / noise suppression | same | same | same |
| 300 ms padding | alex.morgan | **LEX** | Voica.rasvin |
| 1000 ms padding | alex.morgan | Alex | VOICA dot Rasvin |

With clean audio Voice Live keeps the first letter at 600 ms or more (300 ms loses it). A live call
after the 600 ms change still lost "Alex" ("dot Morgan"), so the rest of the loss likely comes from
the browser path (its audio processing, or how quietly the first word starts). Options: 1000 ms
padding, browser auto gain control off. The phone path skips the browser processing.
