# Reference client

A working Company Central Platform client in about a hundred lines of Node, with
no dependencies.

```bash
CCP_URL=https://company-central-platform-production.up.railway.app \
CCP_CLIENT_ID=ccp_… \
CCP_CLIENT_SECRET=ccps_… \
node client.mjs
```

## What it is for

Not a library. It is short enough to read in one sitting and exists to show the
four things every integration gets wrong first time:

| | Why it matters |
|---|---|
| **The token is cached** | Fetching one per request hits the token endpoint's rate limit, which is deliberately tight — that endpoint is where somebody guesses at your secret. |
| **It refreshes early** | Sixty seconds before expiry. Waiting for a 401 works until a request is in flight across the boundary, and then it fails once, unreproducibly. |
| **429 is honoured** | `Retry-After` is not advice. Retrying immediately is refused again and lengthens the queue behind you. |
| **Errors match on `code`** | Messages are translated into Arabic and English and reworded freely. Codes are part of the contract. |

It also logs a warning when a response carries a `Sunset` header — one line,
which is the difference between a planned migration and an outage.

## What it deliberately does not do

- **No retry on 5xx.** Whether a failed write is safe to repeat depends on the
  operation, and a blanket retry in a sample would be copied into a system where
  it duplicates something.
- **No token storage across processes.** In memory, per process. A shared cache
  is a reasonable optimisation and a bad default.
- **No credentials in the file.** They come from the environment — not from a
  file in your repository, and not from an argument that lands in your shell
  history.

## Next

[`docs/development/integration-guide.md`](../../docs/development/integration-guide.md)
covers registration, permission manifests, acting on behalf of a person, and the
checklist worth going through before a system goes live.
