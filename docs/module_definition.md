# Module Definition

This document defines what a module is, what properties every module must have, and what rules govern
its boundaries. It applies to any software system regardless of language, platform, or size.

---

## 1. Definition

A **module** is a named, bounded unit of a software system that:

- Encapsulates a coherent set of capabilities that belong together because they are governed by the
  same Postulate.
- Has exactly one **Postulate**: the specific external force whose requirements are the
  exclusive and sufficient source of all changes to the module.
- Communicates with other modules only through explicit, declared contracts.
- Maps to exactly one namespace. No capability the module owns may live outside that namespace.
- Is the sole owner of every capability listed in its In Scope. No other module may claim or perform
  those capabilities.
- Owns the schema of every data structure listed in its Data Ownership. Data structures are
  first-class owned artifacts, not byproducts of module logic. No data structure a module owns may
  be defined or redefined outside that module's namespace.

A module is **not** a layer (e.g., "data access", "presentation"). Layers group code by technical
role. A module groups code by the force that drives it to change.

---

## 2. Required Properties

Every module definition must include all of the following properties. A module definition is incomplete
if any property is absent or does not satisfy its constraint.

| Property                 | Constraint                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
|--------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Name**                 | A unique noun phrase that names the capability, not the technology that implements it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| **Namespace**            | Exactly one namespace. No other module maps to this namespace.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| **Responsibility**       | One sentence. Active voice. No conjunctions. If "and" is required to complete the sentence, the module has two responsibilities and must be split or its responsibility restated at a higher level of abstraction.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| **Postulate**     | One named external force. Must be specific enough that any proposed change to the module can be definitively answered: does this change originate from this authority, yes or no? If the answer would be "maybe", the Postulate is not specific enough.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| **In Scope**             | An explicit list of capabilities this module owns. Each item must appear in exactly one module's In Scope list across the entire system.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| **Data Ownership**       | An explicit list of data structures (types, records, schemas) this module defines and owns. Each structure's schema is governed exclusively by this module's Postulate. Each structure must appear in exactly one module's Data Ownership list across the entire system. Other modules may reference these structures but may not redefine or extend them.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| **Out of Scope**         | An explicit list of capabilities this module must not perform. This list is not a catch-all. It names the specific capabilities most likely to be mistakenly placed here.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| **Why**                  | The structural reason this boundary exists as a separate module. Must name the specific problem the boundary solves. "Separation of concerns", "clarity", and "correctness" are not structural reasons.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| **Contracts**            | The explicit interfaces through which other modules interact with this module, of two kinds: **behavioral** (callable interfaces: methods, delegates, events) and **data** (data structures crossing this module's boundary). Behavioral contracts are declared as inbound or outbound. Data contracts are declared as two named sub-lists: **Data, outbound** — structures owned by this module that other modules reference, with each referencing module named explicitly; **Data, inbound** — structures owned by other modules that this module references, with each owning module named explicitly. Both sub-lists must be declared. An empty sub-list must be declared explicitly as `none`; an absent sub-list fails validation. `none` is an affirmative statement that the author confirmed no data crosses this boundary in that direction — it cannot be inferred from omission. Other modules must not access this module's internals. |
| **Dependency Direction** | The declared list of other modules this module may depend on. Must name modules, not capabilities, layers, or technologies.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |

---

## 3. Rules

The following rules are unconditional. There are no exceptions.

**Rule 1.** A module must have exactly one Postulate. If naming the Postulate requires
a conjunction ("the OS and the business rules"), it describes two Change Authorities. The module must
be split.

**Rule 2.** A module must map to exactly one namespace. Every capability in its In Scope must reside
in that namespace.

**Rule 3.** No capability in a module's In Scope may appear in any other module's In Scope. In Scope
lists must be mutually exclusive across all modules in the system.

**Rule 4.** A module must not perform any work listed in another module's In Scope. Work that belongs
to another module must be obtained through that module's declared contracts.

**Rule 5.** The dependency direction between modules must be acyclic. If module A declares a dependency
on module B, then module B must not declare a dependency on module A, directly or transitively.

**Rule 6.** A module's Why must state a structural reason — the specific problem solved by having this
boundary. "Good architecture", "easier to understand", and "testability" are not structural reasons.
A structural reason identifies what would break, couple, or duplicate if the boundary did not exist.

**Rule 7.** A module's Responsibility must be expressible in one sentence without conjunctions. An
orchestration module is valid only when its sole responsibility is to sequence or coordinate other
modules. In that case, the Responsibility may name the modules being coordinated.

**Rule 8.** Each data structure has exactly one owning module. The owning module is the one whose
Postulate governs changes to the structure's schema. If a proposed schema change cannot be
traced to the owning module's Postulate, ownership is misassigned and must be corrected.

**Rule 9.** A module that references a data structure owned by another module has a data dependency
on that module. Data dependencies must appear in the module's Dependency Direction and are subject
to the same acyclicity requirement as behavioral dependencies (Rule 5).

**Rule 10.** A module must not define a data structure whose schema is driven by a different
module's Postulate. If a proposed change to a structure's schema originates from a force
other than this module's Postulate, the structure belongs to the module whose Change
Authority governs it.

---

## 4. Validation

A module definition is validated at two distinct scopes. A definition is not accepted until both
scopes pass.

### Scope 1 — Per-module

Each question is answerable by reading only this module's definition. No other module's definition
is required.

- Is the Postulate named specifically enough that any proposed change can be definitively
  traced to it (yes) or ruled out from it (no)? If the honest answer is "it depends", the Change
  Authority is not specific enough and must be rewritten.
- Does the Responsibility contain no conjunctions? If "and" is required for the sentence to be
  complete, the module has two responsibilities.
- Does the Why name the specific structural problem that the boundary solves, rather than a general
  quality attribute (clarity, correctness, maintainability)?
- Does the Dependency Direction list name only other modules, not capabilities, layers, or
  technologies?
- For each data structure in Data Ownership: can every proposed schema change be definitively
  traced to this module's Postulate? If the honest answer is "it depends", ownership is
  misassigned.
- Does the Contracts section declare all data structures that cross this module's boundary —
  both those this module owns and those it receives from other modules?
- Are both `Data, outbound` and `Data, inbound` sub-lists present, each declared either with
  named entries or explicitly as `none`? An absent sub-list is not equivalent to `none`.

### Scope 2 — System-level

Each question requires all module definitions to be present.

- Does each item in this module's In Scope appear in exactly one module's In Scope across the entire
  system (this one)?
- For each module listed in this module's Dependency Direction: does that module's Dependency
  Direction not include this module, directly or transitively?
- Does each data structure in this module's Data Ownership appear in exactly one module's Data
  Ownership list across the entire system?
- For every module that references a data structure in this module's Data Ownership: does that
  module declare a data dependency on this module in its Dependency Direction?
- For every data structure declared as outbound to a named module in this module's Contracts: does
  that named module declare the same structure as inbound from this module?
- For every data structure declared as inbound from a named module in this module's Contracts: does
  that named module declare the same structure as outbound to this module?

---

## 5. Sub-modules and Module Areas

A **module area** is a module whose context is large enough to contain distinct implementation
obligations requiring their own Postulates. A module area:

- Owns the bare namespace prefix. Sub-modules may not claim it.
- Holds its own capabilities and data structures, governed by its own Postulate.
- Is the parent context for one or more sub-modules.
- Is a module — all module rules (Sections 1–4) apply to it without exception.

A **sub-module** is a module that lives within a module area. A sub-module:

- Has a Postulate that is a more specific implementation obligation within the module area's Postulate.
- Exists specifically to serve the parent context — it would not exist independently of it.
- Is a full module — all module rules (Sections 1–4) apply to it without exception.

A module area's primary work belongs to the module area itself. When a component IS the module area
doing its primary work, it belongs in the module area, not in a sub-module. A sub-module is
appropriate when a component is a specialist the module area delegates to — one whose obligation
is specific enough to bound and validate independently.

### Additional rules for sub-modules

**Rule SR1.** Every item in a sub-module's In Scope must fall within the scope of its parent module
area. A sub-module must not claim a capability outside its parent's declared scope.

**Rule SR2.** A sub-module's Postulate must be a more specific implementation obligation within the
module area's Postulate. It may be more granular. It must not be broader.

**Rule SR3.** Sharing a module area does not grant sibling sub-modules special access to each other.
All cross-sub-module interactions are governed by declared contracts and dependency direction rules,
the same as any two independent modules.

**Rule SR4.** A module within a module area that orchestrates sibling sub-modules must have its own
Postulate — the policy governing how the sub-modules are sequenced or coordinated. This Postulate is
distinct from the Postulate of any sub-module it orchestrates.

**Rule SR5.** A sub-module may only own data structures whose schema is governed by that sub-module's
own Postulate. A data structure may not be claimed in both a module area and a sub-module's Data
Ownership simultaneously.

**Rule SR6.** Each sub-module's namespace is formed by appending a unique suffix to the module area's
namespace prefix. The bare prefix is the module area's namespace. Sub-modules must not claim it.

### When to use a sub-module

A sub-module is warranted when a component within the module area:
1. Has a Postulate distinct enough from the module area's Postulate to bound and validate independently.
2. Would not exist outside the module area's context.
3. Is not doing the module area's primary work — it is a specialist the module area delegates to.

If creating a sub-module for a component would obscure that the component IS the module area doing
its primary work, the component belongs in the module area.

---

## 6. Examples

Each example uses a generic problem space. No example is tied to a specific technology or project.

---

### Example A — Valid module

**Name:** Email Delivery

**Namespace:** `Notifications.Email`

**Responsibility:** Transmit outbound email messages through the configured mail provider.

**Postulate:** The external mail provider's API and authentication protocol.

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
  module's declared type; the schema is governed by what the mail provider's API requires
- Data, outbound: `DeliveryResult` to Notification Templating — the outcome of a Send call;
  schema governed by what the mail provider reports back
- Data, inbound: none

**Dependency Direction:** depends on `Notifications.Policy` for retry configuration

**Validation — Scope 1:**

- Postulate: "The external mail provider's API and authentication protocol" — any proposed
  change (new auth header, changed endpoint, dropped TLS version) is traceable to the mail provider.
  A change to email template HTML is not traceable to the mail provider. ✓
- Responsibility: no conjunctions ✓
- Why: structural — names what would couple if the boundary did not exist ✓
- Dependency Direction: names a module, not a layer or technology ✓
- Data Ownership: `Message` and `DeliveryResult` — all proposed schema changes (new header field
  required by provider, new failure reason code returned by provider) are traceable to the mail
  provider's API. Changes driven by template content or caller preferences are not traceable here. ✓
- Contracts: behavioral and data contracts are declared; both sub-lists present. `Message` and
  `DeliveryResult` are outbound to Notification Templating (Email Delivery owns both schemas;
  Notification Templating conforms to them). The symmetric check requires Notification Templating
  to declare both structures as inbound from Email Delivery. `Data, inbound: none` is an
  affirmative declaration that no foreign-owned schema crosses this boundary inbound. ✓

**Validation — Scope 2 (assuming no other module claims these In Scope items or Data Ownership):** ✓

---

### Example B — Invalid: vague Postulate

**Name:** Communication

**Postulate:** Business needs

---

**Problem:** "Business needs" is not a Postulate. It cannot answer the question "does this
proposed change originate from this authority?" for any specific change request. Every change in the
system could be described as originating from "business needs".

**Consequence:** Rule 1 is violated. The module cannot be validated.

**Fix:** Identify what specific business process, external system, or organizational unit actually
drives the changes to this module. If multiple distinct forces exist, split the module.

---

### Example C — Invalid: overlapping In Scope

**System context:** Two modules are defined.

**Module X — Notification Templating**
In Scope includes: *email template rendering*

**Module Y — Email Delivery**
In Scope includes: *email template rendering*

---

**Problem:** "Email template rendering" appears in two In Scope lists. Rule 3 is violated.

**Consequence:** When a change to email template rendering is required, it is unclear which module
owns the change. Both modules will drift independently. Tests in both modules will make assumptions
about who is responsible.

**Fix:** Remove "email template rendering" from exactly one module's In Scope. If both modules
genuinely perform it, one is performing work it does not own (Rule 4 is also violated).

---

### Example D — Invalid: cyclic dependency

**System context:** Three modules are defined.

**Module A — Order Management**
Dependency Direction: depends on Module B (Inventory)

**Module B — Inventory**
Dependency Direction: depends on Module C (Pricing)

**Module C — Pricing**
Dependency Direction: depends on Module A (Order Management)

---

**Problem:** The dependency graph contains a cycle: A → B → C → A. Rule 5 is violated.

**Consequence:** No module can be understood, tested, or deployed independently. A change in any
one module forces analysis of all three. The system cannot be reasoned about in parts.

**Fix:** Identify which dependency is the incorrect one. The direction in which data and control
legitimately flow determines the correct dependency direction. Reverse or remove the offending
dependency. If the cycle reflects a genuine bidirectional need, introduce a shared contract or
event that both modules depend on, replacing the direct dependency.

---

### Example E — Invalid: data structure with no declared owner

**System context:** Two modules are defined.

**Module X — Order Fulfillment**
In Scope includes: *picking items from warehouse shelves*
Data Ownership: *(none declared)*
Contracts, inbound: `Fulfill(OrderLine[] lines)` — receives an array of `OrderLine`

**Module Y — Order Management**
In Scope includes: *creating and validating customer orders*
Data Ownership: *(none declared)*
Contracts, outbound: `Fulfill(OrderLine[] lines)` — passes an array of `OrderLine`

---

**Problem:** `OrderLine` crosses the boundary between the two modules but neither module declares
ownership of it. Rule 8 is violated. The schema of `OrderLine` — which fields it carries, which
are required, what the field types are — has no declared Postulate. When a proposed change
arrives ("add a `substitution_allowed` flag to `OrderLine`"), it is unclear which module owns the
change, whether both modules must update together, and which Postulate authorizes it.

**Consequence:** `OrderLine` drifts silently. Module X and Module Y independently interpret and
extend the structure. Each adds fields the other does not expect. Integration breaks at runtime
without any module boundary being visibly crossed. No audit or review process catches this because
no ownership rule was ever declared.

**Fix:** Assign `OrderLine` to exactly one module's Data Ownership. The correct owner is the
module whose Postulate governs what an order line contains. If the structure is driven by
fulfillment rules (warehouse slot identifiers, pick quantities), it belongs to Module X. If it is
driven by customer-facing order semantics (SKU, price, quantity ordered), it belongs to Module Y.
Once assigned, the other module declares a data dependency on the owning module in its Dependency
Direction and references `OrderLine` only through the owning module's declared Data Contracts.
