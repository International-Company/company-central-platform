# Observability — and the runbook that goes with it

Reference: [ARCHITECTURE.md §22](../../ARCHITECTURE.md) · [ADR-015](../architecture/adr/ADR-015-observability.md).

**Three signals, one correlation id.** That is the whole design: logs, traces and
the audit trail all carry the same identifier, so "a user reported an error at
10:14" becomes a single query rather than three searches and a guess.

---

## 1. Turning it on

```
OTEL_EXPORTER_OTLP_ENDPOINT = https://<the provider's collector>
OTEL_EXPORTER_OTLP_HEADERS  = api-key=<the provider's key>
```

**Instrumentation is unconditional; export is not.** With no endpoint set, the
Platform still instruments and sends nothing — which is what a developer machine
should do, and means the code path in production is the one that ran locally.

Nothing is self-hosted. Prometheus, Grafana, Loki and Jaeger deployed to watch
one application would be monitoring infrastructure more complex than the thing it
monitors, and would itself need monitoring (§22.6).

### Sampling

```jsonc
"Observability": { "TraceSampleRatio": 1.0 }
```

Lower it when volume costs more than it is worth. **Sample rather than
instrument less**: turning off instrumentation leaves whole code paths invisible,
while sampling leaves every path visible and some requests unrecorded — much the
better trade at the moment something is wrong.

---

## 2. Finding one request

Every response carries the identifiers back:

```http
X-Correlation-Id: 01JBQ8Z3M4N5P6Q7R8S9T0
X-Request-Id: 0HN7GK2M9V4A1:00000003
```

With one correlation id you can retrieve:

| Where | How |
|---|---|
| Logs | `CorrelationId` on every line of that request |
| Traces | `ccp.correlation_id` on the span |
| Audit | The `correlationId` column |
| Integration calls | `correlation_id` on every outbound call it caused |

A caller may supply its own, so an operation spanning a business system and the
Platform shares one identifier end to end. It is sanitised on the way in — it is
attacker-controlled text that ends up in log files.

---

## 3. What is never logged

Passwords, tokens, refresh tokens, recovery codes, secrets.

**Removed at the sink**, not left to the discipline of whoever writes each log
statement. Every leak of this kind is written by somebody being careful who did
not know that the object they logged carried a token three properties down, or
that the exception message contained the request body — and discipline does not
scale to every log statement anybody will ever write.

Two rules run over every event: a property whose *name* means a credential is
blanked whole, and a *value* that looks like one is blanked wherever it appears.

It is a last line and not the only one. A redactor people rely on instead of not
logging secrets will eventually meet a shape it does not recognise.

---

## 4. The instruments

The framework already emits request rate, duration and error rate per endpoint,
and the database and HTTP clients emit theirs. Instrumenting those again would
produce two numbers for one thing and an argument about which is right.

What the Platform adds is what the framework cannot know:

| Metric | Answers |
|---|---|
| `ccp.authentication.attempts` | Is this a failure spike, or is everybody signing in? |
| `ccp.authorization.denials` | Is somebody mapping what they can reach? |
| `ccp.ratelimit.rejections` | Is a limit set wrongly, or is this an attack? |
| `ccp.jobs.runs` / `ccp.jobs.duration` | Did the sweep run, and is it getting slower? |
| `ccp.outbox.dispatches` | Are events getting out? |

Authentication is counted as **attempts with an outcome tag** rather than as
failures. A failure count alone cannot distinguish a spike from a busy Monday,
and that ratio is what an alert actually watches.

Denials are tagged by **permission and not by caller**. A caller id would put a
person's identifier into a metrics backend, which is not a place personal data
belongs — and the permission is what tells you whether a burst is somebody
probing.

---

## 5. Health

| Endpoint | Answers | Who calls it |
|---|---|---|
| `/health/live` | Is the process alive? | The container platform |
| `/health/ready` | Can it serve? | The load balancer |

Liveness returns no detail at all. It tells an attacker nothing beyond "the
process is running", which is what a probe needs and all it needs.

Readiness checks dependencies:

| Dependency | Failing means |
|---|---|
| Database | **Unhealthy** — the Platform cannot serve anything |
| Document storage | **Degraded** — documents stop; identity, authorization and workflow do not |

That distinction is deliberate. Taking the whole Platform out of rotation because
a bucket is unreachable would be the same mistake that once stopped it starting
over a folder it could not create.

---

## 6. Runbook

### The alerts worth waking somebody for

Alerts that fire routinely and are routinely ignored are worse than no alerts.
These are the conditions where a person must act.

| Condition | Threshold | First thing to do |
|---|---|---|
| Readiness failing | 2 consecutive probes | Check which dependency; `/health/ready` names it |
| Error rate | >5% of requests over 5 minutes | Group by endpoint, then by correlation id |
| Authentication failures | >50% of attempts over 10 minutes | Is one account targeted, or many? |
| Rate-limit rejections | 10× the daily baseline | Which policy — an attack, or a limit set wrongly? |
| Outbox lag | >1000 undispatched, or oldest >15 minutes | Is the relay running? Is a handler throwing? |
| Background job failures | Any 3 consecutive | Which sweep; its log line names the exception |
| Integration circuit open | Any provider, >5 minutes | The provider is down, or the credential expired |

### A request failed and the user has the id

1. Search logs for the correlation id → the request, its status, its duration.
2. Open the trace on the same id → which span was slow or threw.
3. If it changed data, the audit trail on the same id → what changed and who.
4. If it called outside, `GET /api/v1/integrations/calls?...` → what was sent and
   what came back, with sensitive fields already blanked.

### The Platform will not start

Read the first fifty lines of the container log. The composition root fails fast
and says which piece of configuration is missing. The failures seen so far were a
connection string absent and a directory that could not be created — both of
which now name themselves.

### Documents disappear after a restart

Object storage is not configured, so files went to a temporary directory. The
log says so on every restart. See [`railway.md`](railway.md).

### An integration is failing

`GET /api/v1/integrations/providers/health` reports each provider from its recent
calls. `Idle` means nothing has been asked of it — not that it is well. Then the
call log, filtered by outcome, shows whether the Platform was refused, timed out,
or never called at all because the circuit was open.

---

## 7. What is measured and not yet watched

Background job runs and durations are recorded as metrics; there is no stored
history of runs and no screen showing them. That belongs with the Platform
dashboard, and is recorded as debt rather than implied — a metric with no
dashboard is a metric somebody has to write a query for during the incident.
