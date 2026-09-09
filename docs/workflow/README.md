# Workflow — the approval engine

A reusable approval engine. It understands states, transitions, assignees,
actions and timers. **It does not understand what is being approved.**

That sentence is the whole design. Everything below follows from it.

---

## 1. What the engine holds, and what it refuses to

| The engine decides | The calling application decides |
|---|---|
| Which step a request is at | What the request *is* |
| Whether an action is permitted there | Whether the amount is over a threshold |
| Who must act, by their place in the company | Who must act, given the business facts |
| That something was approved | What approval *means* |
| That a deadline passed | What to do about it |

There is no threshold in this module, no amount, no currency and no eligibility
rule — and an architecture test fails the build if that vocabulary appears in
any of its source files. The test exists because the erosion is gradual: nobody
decides to put a purchase limit in a general engine; somebody adds one at five
o'clock because the alternative is a conversation, and a year later the module
is the purchasing system's approval logic wearing a general name.

---

## 2. Registering a process

A definition is data. Your application registers it once per version, through
the API, and the Platform needs no code change to run it.

```http
POST /api/v1/workflow/definitions
Authorization: Bearer <token with platform.workflow.manage>

{
  "applicationCode": "purchasing",
  "code": "purchase-approval",
  "version": 1,
  "nameAr": "اعتماد أمر شراء",
  "nameEn": "Purchase approval",
  "initialStepKey": "manager-review",
  "steps": [
    {
      "key": "manager-review",
      "nameAr": "مراجعة المدير",
      "nameEn": "Manager review",
      "order": 1,
      "assigneeStrategy": "RequesterManager",
      "serviceLevelHours": 48,
      "transitions": [
        { "action": "Approve", "targetStepKey": "finance-approval" },
        { "action": "Reject",  "targetStepKey": null }
      ]
    },
    {
      "key": "finance-approval",
      "nameAr": "اعتماد المالية",
      "nameEn": "Finance approval",
      "order": 2,
      "assigneeStrategy": "Position",
      "assigneePositionId": "…",
      "transitions": [
        { "action": "Approve", "targetStepKey": null },
        { "action": "Return",  "targetStepKey": "manager-review" },
        { "action": "Reject",  "targetStepKey": null }
      ]
    }
  ]
}
```

A `targetStepKey` of `null` ends the instance, and the action decides how:
`Approve` completes it approved, `Reject` rejected, `Cancel` cancelled. `Return`
**must** name a target — returning means "go back and fix this", and going back
to nowhere is not an ending.

### Validation happens at registration

Every transition target must resolve, and every step must be reachable from the
initial one. A process that dead-ends is refused when it is written rather than
discovered by whoever is waiting on step three.

### Versions are frozen

A published version cannot gain a step or change a transition. To change a
process, register the next version. **Instances continue on the version they
started with** — an approval whose rules changed underneath it is an approval
nobody can account for, and "why did this go to Amira?" must still have an
answer a year later.

Retiring a version stops new instances. Running ones are untouched.

---

## 3. Choosing who acts

Six strategies, and every one of them answers *which person, by their place in
the company*:

| Strategy | Resolves to |
|---|---|
| `User` | One named account |
| `Role` | Everyone holding a role, live grants only |
| `Position` | Everyone holding a job position who has an account |
| `RequesterManager` | The requester's direct manager, **resolved when the step is entered** |
| `UnitHead` | The employee at the top of a unit's own reporting line |
| `SuppliedByCaller` | Whoever your application named when it started the instance |

`RequesterManager` resolves late on purpose: a request that has waited three
weeks should reach the manager the person has today, not the one they had when
they filed it.

**`SuppliedByCaller` is where business-conditional routing lives.** If purchases
over a threshold must go to the finance director, your application works that
out — it has the amount, it has the policy — and supplies the answer:

```http
POST /api/v1/workflow/instances

{
  "applicationCode": "purchasing",
  "definitionCode": "purchase-approval",
  "resourceType": "purchase-order",
  "resourceId": "PO-2026-0041",
  "assignees": ["…"]
}
```

### If nobody resolves, the request is refused

A step whose role, position or manager resolves to nobody would leave a request
waiting forever. The engine refuses to enter such a step and says so, at the
moment the request would begin — rather than the person discovering it three
weeks later.

---

## 4. What the Platform stores about your record

Two strings: `resourceType` and `resourceId`. Both opaque.

The Platform records that `purchase-order/PO-2026-0041` was approved. It does
not know what a purchase order is, cannot read one, and has no opinion about
what approval means for it. There is no foreign key from the workflow schema to
anything, and a column named after your record is exactly the change that would
end the module's reusability.

---

## 5. Finding out what happened

Subscribe to the integration events. They are staged in the transactional outbox
and delivered at least once, so **handlers must be idempotent on `eventId`**.

| Event | When |
|---|---|
| `workflow.instance.started` | A request began |
| `workflow.step.advanced` | It moved from one step to the next |
| `workflow.instance.completed` | It finished — `outcome` is `Approved`, `Rejected` or `Cancelled` |
| `workflow.task.assigned` | Somebody has something to do |
| `workflow.task.escalated` | A task passed its service level |
| `workflow.task.delegated` | A task was handed on |

`workflow.instance.completed` is the one most applications wait for. It carries
your `resourceType` and `resourceId`, the definition code **and version**, and
the outcome — everything needed to act without a lookup against a definition
that may since have been retired.

Or poll, if that suits you better:

```http
GET /api/v1/workflow/instances?resourceType=purchase-order&resourceId=PO-2026-0041
```

---

## 6. Acting on a task

People act through their own inbox. Only the assignee can act, checked in the
handler and again in the aggregate — one check is one place to forget it.

```http
GET  /api/v1/me/tasks
POST /api/v1/me/tasks/{id}/actions   { "action": "Approve", "comment": "…" }
```

Each inbox row carries the actions the process actually permits at that step, so
a screen offers real buttons rather than every verb the engine knows.

**The actor comes from the token, never the body.** A caller who could name
themselves could approve in somebody else's name, and the trail would attribute
a decision to the wrong person.

Delegation moves the task, not the instance: the step does not advance, and the
trail reads "Amira delegated to Faisal, Faisal approved" rather than leaving
Faisal's authority unexplained.

When several people could take a step, the first to act settles it and the rest
are **withdrawn** — not completed. Nobody acted on them, and a report of who
approved what must not count them.

---

## 7. Deadlines

A step may carry `serviceLevelHours`. A task that passes it is escalated once by
a background sweep, which raises `workflow.task.escalated` and marks the task.

**Escalation does not reassign.** Moving somebody's work to their manager
automatically is a company policy, not an engine behaviour; deciding it here
would be the Platform making an organizational choice on the company's behalf.
The event says what happened, and Notifications tells whoever should know.

Once, not repeatedly: a timer that fires every sweep sends a reminder every five
minutes until somebody acts, and people learn to filter it — at which point the
escalation has made the problem harder to see rather than easier.

---

## 8. Permissions

| Permission | For |
|---|---|
| `platform.workflow.manage` | Registering and retiring processes |
| `platform.workflow.start` | Starting and cancelling requests |
| `platform.workflow.view` | Reading processes and instances |
| *(none)* | Your own inbox, and acting on your own tasks |

Reading and acting on one's own tasks needs no permission. Gating it would mean
granting that permission to everybody, which makes it meaningless — and being
the assignee *is* the authorisation.

---

## 9. The seams

`IAssigneeResolver` is declared in the Application layer and implemented in
Infrastructure. Resolving "the requester's manager" means reading the
organizational structure, and an engine referencing Organization directly would
be tied to one company's model.

The implementation reaches `IOrganizationDirectory` and `IRoleDirectory` — both
Contracts-only, both narrow. An architecture test refuses any reference from
this module to another module's Domain, Application or Infrastructure.

**Lift the engine into another product and `PlatformAssigneeResolver` is the
only file that needs rewriting.**
