# Domain Definition

This document defines what a domain is, what properties every domain must have, and what rules govern
its boundaries. It applies to any software system regardless of language, platform, or size.

---

## 1. Definition

A **domain** is a named, bounded unit of a software system that:

- Encapsulates a coherent set of capabilities that belong together because they are governed by the
  same Change Authority.
- Has exactly one **Change Authority**: the specific external force whose requirements are the
  exclusive and sufficient source of all changes to the domain.
- Communicates with other domains only through explicit, declared contracts.
- Maps to exactly one namespace. No capability the domain owns may live outside that namespace.
- Is the sole owner of every capability listed in its In Scope. No other domain may claim or perform
  those capabilities.
- Owns the schema of every data structure listed in its Data Ownership. Data structures are
  first-class owned artifacts, not byproducts of domain logic. No data structure a domain owns may
  be defined or redefined outside that domain's namespace.

A domain is **not** a layer (e.g., "data access", "presentation"). Layers group code by technical
role. A domain groups code by the force that drives it to change.

---

## 2. Required Properties

Every domain definition must include all of the following properties. A domain definition is incomplete
if any property is absent or does not satisfy its constraint.

| Property                 | Constraint                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
|--------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Name**                 | A unique noun phrase that names the capability, not the technology that implements it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| **Namespace**            | Exactly one namespace. No other domain maps to this namespace.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| **Responsibility**       | One sentence. Active voice. No conjunctions. If "and" is required to complete the sentence, the domain has two responsibilities and must be split or its responsibility restated at a higher level of abstraction.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| **Change Authority**     | One named external force. Must be specific enough that any proposed change to the domain can be definitively answered: does this change originate from this authority, yes or no? If the answer would be "maybe", the Change Authority is not specific enough.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| **In Scope**             | An explicit list of capabilities this domain owns. Each item must appear in exactly one domain's In Scope list across the entire system.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| **Data Ownership**       | An explicit list of data structures (types, records, schemas) this domain defines and owns. Each structure's schema is governed exclusively by this domain's Change Authority. Each structure must appear in exactly one domain's Data Ownership list across the entire system. Other domains may reference these structures but may not redefine or extend them.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| **Out of Scope**         | An explicit list of capabilities this domain must not perform. This list is not a catch-all. It names the specific capabilities most likely to be mistakenly placed here.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| **Why**                  | The structural reason this boundary exists as a separate domain. Must name the specific problem the boundary solves. "Separation of concerns", "clarity", and "correctness" are not structural reasons.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| **Contracts**            | The explicit interfaces through which other domains interact with this domain, of two kinds: **behavioral** (callable interfaces: methods, delegates, events) and **data** (data structures crossing this domain's boundary). Behavioral contracts are declared as inbound or outbound. Data contracts are declared as two named sub-lists: **Data, outbound** — structures owned by this domain that other domains reference, with each referencing domain named explicitly; **Data, inbound** — structures owned by other domains that this domain references, with each owning domain named explicitly. Both sub-lists must be declared. An empty sub-list must be declared explicitly as `none`; an absent sub-list fails validation. `none` is an affirmative statement that the author confirmed no data crosses this boundary in that direction — it cannot be inferred from omission. Other domains must not access this domain's internals. |
| **Dependency Direction** | The declared list of other domains this domain may depend on. Must name domains, not capabilities, layers, or technologies.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |

---

## 3. Rules

The following rules are unconditional. There are no exceptions.

**Rule 1.** A domain must have exactly one Change Authority. If naming the Change Authority requires
a conjunction ("the OS and the business rules"), it describes two Change Authorities. The domain must
be split.

**Rule 2.** A domain must map to exactly one namespace. Every capability in its In Scope must reside
in that namespace.

**Rule 3.** No capability in a domain's In Scope may appear in any other domain's In Scope. In Scope
lists must be mutually exclusive across all domains in the system.

**Rule 4.** A domain must not perform any work listed in another domain's In Scope. Work that belongs
to another domain must be obtained through that domain's declared contracts.

**Rule 5.** The dependency direction between domains must be acyclic. If domain A declares a dependency
on domain B, then domain B must not declare a dependency on domain A, directly or transitively.

**Rule 6.** A domain's Why must state a structural reason — the specific problem solved by having this
boundary. "Good architecture", "easier to understand", and "testability" are not structural reasons.
A structural reason identifies what would break, couple, or duplicate if the boundary did not exist.

**Rule 7.** A domain's Responsibility must be expressible in one sentence without conjunctions. An
orchestration domain is valid only when its sole responsibility is to sequence or coordinate other
domains. In that case, the Responsibility may name the domains being coordinated.

**Rule 8.** Each data structure has exactly one owning domain. The owning domain is the one whose
Change Authority governs changes to the structure's schema. If a proposed schema change cannot be
traced to the owning domain's Change Authority, ownership is misassigned and must be corrected.

**Rule 9.** A domain that references a data structure owned by another domain has a data dependency
on that domain. Data dependencies must appear in the domain's Dependency Direction and are subject
to the same acyclicity requirement as behavioral dependencies (Rule 5).

**Rule 10.** A domain must not define a data structure whose schema is driven by a different
domain's Change Authority. If a proposed change to a structure's schema originates from a force
other than this domain's Change Authority, the structure belongs to the domain whose Change
Authority governs it.

---

## 4. Validation

A domain definition is validated at two distinct scopes. A definition is not accepted until both
scopes pass.

### Scope 1 — Per-domain

Each question is answerable by reading only this domain's definition. No other domain's definition
is required.

- Is the Change Authority named specifically enough that any proposed change can be definitively
  traced to it (yes) or ruled out from it (no)? If the honest answer is "it depends", the Change
  Authority is not specific enough and must be rewritten.
- Does the Responsibility contain no conjunctions? If "and" is required for the sentence to be
  complete, the domain has two responsibilities.
- Does the Why name the specific structural problem that the boundary solves, rather than a general
  quality attribute (clarity, correctness, maintainability)?
- Does the Dependency Direction list name only other domains, not capabilities, layers, or
  technologies?
- For each data structure in Data Ownership: can every proposed schema change be definitively
  traced to this domain's Change Authority? If the honest answer is "it depends", ownership is
  misassigned.
- Does the Contracts section declare all data structures that cross this domain's boundary —
  both those this domain owns and those it receives from other domains?
- Are both `Data, outbound` and `Data, inbound` sub-lists present, each declared either with
  named entries or explicitly as `none`? An absent sub-list is not equivalent to `none`.

### Scope 2 — System-level

Each question requires all domain definitions to be present.

- Does each item in this domain's In Scope appear in exactly one domain's In Scope across the entire
  system (this one)?
- For each domain listed in this domain's Dependency Direction: does that domain's Dependency
  Direction not include this domain, directly or transitively?
- Does each data structure in this domain's Data Ownership appear in exactly one domain's Data
  Ownership list across the entire system?
- For every domain that references a data structure in this domain's Data Ownership: does that
  domain declare a data dependency on this domain in its Dependency Direction?
- For every data structure declared as outbound to a named domain in this domain's Contracts: does
  that named domain declare the same structure as inbound from this domain?
- For every data structure declared as inbound from a named domain in this domain's Contracts: does
  that named domain declare the same structure as outbound to this domain?

---

## 5. Subdomains

A **subdomain** is a domain that lives within a named **domain area**. A domain area is a logical
grouping — it does not own capabilities or data structures, and it does not map to a namespace.
Its subdomains own all capabilities and data structures. Each subdomain's namespace is formed by
appending a unique suffix to the domain area's namespace prefix. The domain area's prefix is
reserved: no subdomain may claim the bare prefix as its namespace if any sibling subdomain exists
under it.

A subdomain is a full domain. All domain rules apply to it without exception.

### Additional rules for subdomains

**Rule S1.** Every item in a subdomain's In Scope must fall within the scope of its parent domain
area. A subdomain must not claim a capability outside its parent's declared scope.

**Rule S2.** A subdomain's Change Authority must be at least as specific as the parent domain area's
Change Authority. It may be more granular. It must not be broader.

**Rule S3.** Sharing a parent domain area does not grant sibling subdomains special access to each
other. All cross-subdomain interactions are governed by declared contracts and dependency direction
rules, the same as any two independent domains.

**Rule S4.** If a parent domain area contains a domain whose responsibility is to orchestrate sibling
subdomains, that orchestration domain must have its own Change Authority, distinct from the Change
Authority of any sibling it orchestrates. The Change Authority for an orchestration domain is the
policy that governs how the subdomains are sequenced or coordinated, not the policy that governs any
individual subdomain's behavior.

**Rule S5.** Data ownership follows the same rules as capability ownership within a domain area.
A subdomain may only own data structures whose schema is governed by that subdomain's own Change
Authority. A data structure may not be claimed in both a parent domain area and a subdomain's
Data Ownership simultaneously.

**Rule S6.** Each subdomain must have a namespace that is distinct from every sibling subdomain's
namespace and from the domain area's bare namespace prefix. The domain area prefix is a reserved
grouping identifier. It is not a valid namespace for any subdomain unless that subdomain is the
sole subdomain in the area, and even then the bare prefix must not be reused if further subdomains
are added later. When a second subdomain is introduced, the first must be renamed to a suffixed
namespace.

### When to use a subdomain

A subdomain is appropriate when a set of domains shares a parent Change Authority — a broader force
that governs all of them — and each has a more specific Change Authority within that parent. The parent
domain area name communicates this shared lineage.

If there is no shared parent Change Authority, the domains are standalone and must not be grouped
under a parent area.

---

## 6. Examples

Each example uses a generic problem space. No example is tied to a specific technology or project.

---

### Example A — Valid domain

**Name:** Email Delivery

**Namespace:** `Notifications.Email`

**Responsibility:** Transmit outbound email messages through the configured mail provider.

**Change Authority:** The external mail provider's API and authentication protocol.

**In Scope:**

- SMTP/API connection management
- Message formatting for transmission (headers, encoding)
- Retry and failure handling for provider errors
- Delivery status reporting

**Data Ownership:**

- `Message` — the fully formed message record (fields: recipient address, subject, body, headers)
  whose structure is governed by what the mail provider's API requires
- `DeliveryResult` — the delivery outcome record (fields: succeeded, failure reason) whose
  structure is governed by what the mail provider reports back on transmission attempts

**Out of Scope:**

- Email template content (owned by Notification Templating)
- Recipient address resolution (owned by Contact Management)
- Delivery preference rules (owned by Notification Policy)

**Why:** The mail provider's API has its own versioning, authentication mechanism, and transport
constraints. Isolating those details means a provider change requires changes only here, not in any
component that generates or routes notifications.

**Contracts:**

- Behavioral, inbound: `IMessageDispatcher.Send(Message message) → DeliveryResult` — accepts a
  fully formed message and returns a delivery outcome
- Behavioral, outbound: none
- Data, outbound: `Message` to Notification Templating — callers must construct it using this
  domain's declared type; the schema is governed by what the mail provider's API requires
- Data, outbound: `DeliveryResult` to Notification Templating — the outcome of a Send call;
  schema governed by what the mail provider reports back
- Data, inbound: none

**Dependency Direction:** depends on `Notifications.Policy` for retry configuration

**Validation — Scope 1:**

- Change Authority: "The external mail provider's API and authentication protocol" — any proposed
  change (new auth header, changed endpoint, dropped TLS version) is traceable to the mail provider.
  A change to email template HTML is not traceable to the mail provider. ✓
- Responsibility: no conjunctions ✓
- Why: structural — names what would couple if the boundary did not exist ✓
- Dependency Direction: names a domain, not a layer or technology ✓
- Data Ownership: `Message` and `DeliveryResult` — all proposed schema changes (new header field
  required by provider, new failure reason code returned by provider) are traceable to the mail
  provider's API. Changes driven by template content or caller preferences are not traceable here. ✓
- Contracts: behavioral and data contracts are declared; both sub-lists present. `Message` and
  `DeliveryResult` are outbound to Notification Templating (Email Delivery owns both schemas;
  Notification Templating conforms to them). The symmetric check requires Notification Templating
  to declare both structures as inbound from Email Delivery. `Data, inbound: none` is an
  affirmative declaration that no foreign-owned schema crosses this boundary inbound. ✓

**Validation — Scope 2 (assuming no other domain claims these In Scope items or Data Ownership):** ✓

---

### Example B — Invalid: vague Change Authority

**Name:** Communication

**Change Authority:** Business needs

---

**Problem:** "Business needs" is not a Change Authority. It cannot answer the question "does this
proposed change originate from this authority?" for any specific change request. Every change in the
system could be described as originating from "business needs".

**Consequence:** Rule 1 is violated. The domain cannot be validated.

**Fix:** Identify what specific business process, external system, or organizational unit actually
drives the changes to this domain. If multiple distinct forces exist, split the domain.

---

### Example C — Invalid: overlapping In Scope

**System context:** Two domains are defined.

**Domain X — Notification Templating**
In Scope includes: *email template rendering*

**Domain Y — Email Delivery**
In Scope includes: *email template rendering*

---

**Problem:** "Email template rendering" appears in two In Scope lists. Rule 3 is violated.

**Consequence:** When a change to email template rendering is required, it is unclear which domain
owns the change. Both domains will drift independently. Tests in both domains will make assumptions
about who is responsible.

**Fix:** Remove "email template rendering" from exactly one domain's In Scope. If both domains
genuinely perform it, one is performing work it does not own (Rule 4 is also violated).

---

### Example D — Invalid: cyclic dependency

**System context:** Three domains are defined.

**Domain A — Order Management**
Dependency Direction: depends on Domain B (Inventory)

**Domain B — Inventory**
Dependency Direction: depends on Domain C (Pricing)

**Domain C — Pricing**
Dependency Direction: depends on Domain A (Order Management)

---

**Problem:** The dependency graph contains a cycle: A → B → C → A. Rule 5 is violated.

**Consequence:** No domain can be understood, tested, or deployed independently. A change in any
one domain forces analysis of all three. The system cannot be reasoned about in parts.

**Fix:** Identify which dependency is the incorrect one. The direction in which data and control
legitimately flow determines the correct dependency direction. Reverse or remove the offending
dependency. If the cycle reflects a genuine bidirectional need, introduce a shared contract or
event that both domains depend on, replacing the direct dependency.

---

### Example E — Invalid: data structure with no declared owner

**System context:** Two domains are defined.

**Domain X — Order Fulfillment**
In Scope includes: *picking items from warehouse shelves*
Data Ownership: *(none declared)*
Contracts, inbound: `Fulfill(OrderLine[] lines)` — receives an array of `OrderLine`

**Domain Y — Order Management**
In Scope includes: *creating and validating customer orders*
Data Ownership: *(none declared)*
Contracts, outbound: `Fulfill(OrderLine[] lines)` — passes an array of `OrderLine`

---

**Problem:** `OrderLine` crosses the boundary between the two domains but neither domain declares
ownership of it. Rule 8 is violated. The schema of `OrderLine` — which fields it carries, which
are required, what the field types are — has no declared Change Authority. When a proposed change
arrives ("add a `substitution_allowed` flag to `OrderLine`"), it is unclear which domain owns the
change, whether both domains must update together, and which Change Authority authorizes it.

**Consequence:** `OrderLine` drifts silently. Domain X and Domain Y independently interpret and
extend the structure. Each adds fields the other does not expect. Integration breaks at runtime
without any domain boundary being visibly crossed. No audit or review process catches this because
no ownership rule was ever declared.

**Fix:** Assign `OrderLine` to exactly one domain's Data Ownership. The correct owner is the
domain whose Change Authority governs what an order line contains. If the structure is driven by
fulfillment rules (warehouse slot identifiers, pick quantities), it belongs to Domain X. If it is
driven by customer-facing order semantics (SKU, price, quantity ordered), it belongs to Domain Y.
Once assigned, the other domain declares a data dependency on the owning domain in its Dependency
Direction and references `OrderLine` only through the owning domain's declared Data Contracts.
