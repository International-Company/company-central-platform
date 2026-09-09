# API versioning — what may change, and what may not

Reference: [ADR-008](../architecture/adr/ADR-008-api-strategy.md) · [ARCHITECTURE.md §11](../../ARCHITECTURE.md).

**The version is in the path: `/api/v1/…`.** It is the only version selector, and
there is no header, query parameter or media type that overrides it.

---

## 1. The promise

Inside a version, the Platform will not break a caller that follows the rules
below. That is the whole of what a version means, and it is worth being exact
about it in both directions — a promise that covers everything cannot be kept,
and a promise that covers nothing is not one.

### Changes that will happen without notice

| Change | Why a correct client survives it |
|---|---|
| A new endpoint | Nothing calls it yet |
| A new optional field in a request | It has a default |
| **A new field in a response** | A client that ignores unknown fields is unaffected |
| A new optional query parameter | Absent means what it meant before |
| A new value in an enumeration | See below — this one bites |
| A new error `code` | The status class is unchanged |
| Wording of any human-readable message | Messages are translated and reworded; match on `code` |

**Enumerations grow.** A new workflow action, a new notification channel, a new
document status. A client that switches exhaustively on a string and throws on
anything unrecognised will break on the day one is added — so treat an unknown
value as "something I do not handle", not as an error.

### Changes that will not happen inside a version

- Removing an endpoint, or changing its path or method.
- Removing a field from a response, or renaming one.
- Changing the type of a field, or making an optional response field disappear.
- Making an optional request field required.
- Changing the meaning of an existing value.
- Changing an error `code` for the same condition.
- Narrowing what an existing permission grants.

Any of those needs a new version.

---

## 2. Deprecation

An endpoint on its way out keeps working and starts saying so, in headers the
standards define rather than in a release note nobody reads:

```http
HTTP/1.1 200 OK
Deprecation: Wed, 01 Oct 2026 00:00:00 GMT
Sunset:      Sun, 01 Mar 2027 00:00:00 GMT
Link:        </api/v2/employees>; rel="successor-version"
```

| Header | Means |
|---|---|
| `Deprecation` | The date it was declared obsolete. It still works; build nothing new on it. |
| `Sunset` | The date it stops. ([RFC 8594](https://www.rfc-editor.org/rfc/rfc8594)) |
| `Link` | What to use instead. |

**Both dates, always.** A deprecation with no end date is a note, and notes are
ignored; a sunset with no warning period is a breaking change with extra
paperwork.

The generated OpenAPI document marks the same operations `deprecated: true` and
repeats the dates in the description, so a client generator sees it too.

### How long

**At least six months** between deprecation and sunset, and longer for anything a
business system is likely to have built a workflow around. The clock starts when
the headers first appear in production, not when the decision was taken.

---

## 3. When a new version happens

A new version is expensive for everybody: every consumer has to move, and the
Platform runs both until they have. So `v2` arrives when a breaking change is
genuinely necessary, not when a shape could be tidier.

When it does:

- `v1` and `v2` run side by side, from the same code, for the whole overlap.
- Every `v1` endpoint that changed carries `Deprecation` and `Sunset` from the
  day `v2` ships.
- The overlap is at least six months.
- Nothing is removed from `v1` during the overlap. A version that keeps changing
  while people migrate off it is one they cannot migrate off.

---

## 4. What a client should do

1. **Ignore fields you do not know.** Do not fail on them, do not log them as
   errors, do not round-trip them back unless the endpoint says to.
2. **Match on `code`, never on `message`.** Messages are translated into Arabic
   and English and are reworded freely; codes are part of the contract.
3. **Handle unknown enumeration values** as "not something I handle".
4. **Watch for `Sunset`.** A single log line when the header appears is enough,
   and it is the difference between a planned migration and an outage.
5. **Do not depend on field order, or on the absence of a field.**

---

## 5. The contract itself

`contracts/platform-api.json` is generated from the running application's own
endpoints and committed to the repository. CI regenerates it and fails if the
committed copy has drifted, so it cannot describe an API that no longer exists.

Each operation states the permission it requires, whether a second factor is
demanded, and whether it can be called anonymously — derived from the endpoint
metadata rather than written by hand.

Generate a client from it with whatever your language uses; the frontend in this
repository generates its TypeScript types from exactly this file.
