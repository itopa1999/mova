# Mova Product Requirements Document

**Status:** Draft, derived from the current codebase  
**Generated:** 2026-10-05  
**Product:** MOVA controlled-access wallets

## 1. Summary

MOVA helps customers control when they can access their money. Customers fund a main account, move money into wallets, set their own release rules and schedules, and MOVA keeps that money unavailable until a release is due. When it is due, MOVA releases the money to the configured destination, including a linked bank account.

The product combines account funding and payment-provider integrations with wallet management, a schedule engine, transaction history, and asynchronous payout and notification processing. This document describes the product implied by the implemented domain model, application flows, API surface, and project walkthrough. It is not a substitute for legal, compliance, or payment-provider approval.

## 2. Problem and opportunity

When all of a customer's money is available at once, it is easy to spend money meant for later. Impulse purchases can leave little or nothing when the money is actually needed. A single available balance does not give the customer control over when they can spend.

Mova addresses this by providing:

- A funded main account and wallets that keep money unavailable until release.
- Customer-defined release rules, schedules, and a preview before wallet creation.
- A clear view of money that is locked, released, unused, or withdrawn.
- Payment and ledger records that explain account activity.
- Optional automation for replenishing wallets.

## 3. Users and actors

| Actor | Needs |
|---|---|
| Customer | Sign up securely, fund an account, create and manage wallets, understand balances and activity, and receive money through the chosen destination. |
| Mova operations/admin | Configure feature availability and support operational flows such as virtual-account setup. Access must be role-appropriate and auditable. |
| Payment/banking providers | Deliver funding, verification, and payout outcomes through authenticated provider integrations. |
| Mova background services | Process due releases, payouts, retries, and notifications reliably without requiring a customer to keep the application open. |

## 4. Product principles

1. Let a customer move available main-account funds into wallets with controlled access.
2. Make each wallet's release rules, schedule, status, destination, and balance movements understandable.
3. Process deposits and releases exactly once from the customer's perspective.
4. Keep the customer's total funds accounted for through every state transition.
5. Support bank-linked funding and payout, while retaining wallet and main-balance destinations.
6. Provide useful notifications and activity history without making external delivery part of a financial transaction's critical path.

### Out of scope for this draft

- Credit, lending, investment returns, or interest.
- Merchant checkout or general-purpose commerce.
- Multi-currency balances or FX conversion. The current model defaults to NGN; any wider currency support needs a separate product and compliance decision.
- Regulatory commitments, guaranteed provider settlement times, or a promised financial return.

## 5. Core customer journey

1. The customer registers and verifies account access using a one-time code.
2. The customer signs in and receives an authenticated session.
3. The customer funds the main account through an available funding method. A verified provider event updates the main balance and transaction history.
4. The customer chooses a category or template, enters a target and release amount, selects a schedule and payout destination, and previews the schedule.
5. On confirmation, Mova checks eligibility and available balance, reserves the target amount, creates the wallet and its rule, and schedules its first release as one atomic operation.
6. Mova processes scheduled releases in the background. The customer can review wallet details, upcoming releases, activity, analytics, and payout history.
7. The wallet completes when the target has been released, or can be paused, resumed, broken, or restarted where the corresponding rules permit.
8. If configured, wallet renewal automation can replenish a wallet from the main account subject to the policy's trigger, amount, balance floor, and renewal limit.

## 6. Functional requirements

Priorities: **P0** = essential financial/product flow; **P1** = supporting capability; **P2** = follow-up or dependent on product decisions.

### FR-1: Account lifecycle and access — P0

- Customers can register, check registration availability, verify an account, sign in, refresh or end a session, and recover or change a password.
- Verification and recovery codes expire, are purpose-bound, and cannot be reused after successful consumption.
- Authentication supports the implemented token/session behavior. Customer-only operations must be scoped to the authenticated customer.
- Customers can manage notification preferences and transaction PIN setup, verification, change, and recovery.
- Validation failures are returned as clear, non-sensitive client errors; internal exception details are not exposed to customers.

**Acceptance:** A new customer can complete registration and verification, sign in, and access only their own profile and financial resources. Expired, used, or incorrect verification codes do not authenticate or change account state.

### FR-2: Main-account funding — P0

- A customer can view funding instructions, initiate supported funding flows, and review transactions.
- Supported provider events currently include Paystack and Flutterwave. Only verified successful payment events may credit a customer.
- Provider references are idempotency keys: replaying a successful event must not create a second credit.
- A successful deposit updates the main balance and creates its transaction and ledger records consistently.
- Failed, abandoned, pending, reversed, and successful provider outcomes must remain distinguishable. A non-successful payment must not increase the available balance.
- Funding methods and virtual accounts are associated with the correct customer and provider.

**Acceptance:** For each provider event, the customer is credited at most once. A duplicate webhook returns an acknowledged/already-processed result without a second balance change. A failed or abandoned payment creates no spendable credit.

### FR-3: Bank-account management — P0

- Customers can list available banks, add and verify a bank account, set or use a default account, link an account to a wallet, and remove an account when allowed.
- Bank-account ownership, verification state, consent, currency, and provider recipient identifiers are retained as relevant to funding or payout.
- A bank account may only be used for a customer or wallet that is authorized to use it.

**Acceptance:** An unverified or non-owned bank account cannot be used for a bank payout. A customer cannot replace or remove an account in a way that silently changes another customer's wallet destination.

### FR-4: Wallet setup and schedule preview — P0

- Customers can create a wallet with a name, optional description, category, target amount, release amount, frequency/configuration, start date, and payout destination.
- Customers can use active wallet templates as defaults, then review and edit their proposed wallet configuration.
- Customers can preview a schedule before committing. A preview includes validity, first release, computed end date, sample release dates and amounts, cumulative amount, and any relevant warnings.
- The schedule types defined by the current product documentation are once, daily, weekly, monthly, quarterly, yearly, and custom intervals. A frequency configuration includes the relevant dates/days/months/interval and local release time.
- The API validates the configuration server-side, including amount bounds, dates, frequency-specific fields, currency, and supported destinations.
- Wallet creation must verify that the customer has sufficient available main balance and that any required payout account/consent is present.
- Wallet, wallet rule, initial scheduled release, balance debit, transaction, and ledger records must be committed atomically. No partial wallet or debit is allowed on failure.
- Only the first future release is persisted at creation; the engine creates subsequent releases as earlier releases complete.

**Acceptance:** A valid wallet reserves exactly its target from the main balance and has one initial scheduled release. An invalid schedule, insufficient balance, or missing required bank authorization creates no wallet and leaves the main balance unchanged. The final installment is capped at the remaining target amount.

### FR-5: Wallet balances and lifecycle — P0

- A customer can list wallets and view wallet details, current status, balances, schedule, activity, analytics, linked bank account, releases, and payout history.
- Wallet states include active, paused, completed, closed, and broken.
- Customers can pause/resume a wallet and use supported break/restart flows subject to their financial consequences and eligibility rules.
- Each wallet distinguishes target, funded, locked, available, cumulative released, cumulative withdrawn, and reset amounts. These are state/accounting views of money, not independent sources of funds.
- When a subsequent release replaces the current release window, an unwithdrawn prior available amount is retained as unused rather than discarded.
- Relocking unused funds is a desired lifecycle capability, but its public API availability must be confirmed before it is treated as currently customer-accessible.

**Acceptance:** For every lifecycle operation, the before/after wallet balances reconcile with transaction and ledger history. A paused, completed, closed, or otherwise ineligible wallet is not processed as an active wallet.

### FR-6: Scheduled releases — P0

- A background service identifies releases whose scheduled time is due and processes them safely if multiple workers or retries occur.
- A successful release moves the release amount from locked funds into the current available window, preserves any prior unused amount, records a release transaction and ledger entry, updates release status, and schedules the next release if the target is not yet complete.
- The release amount must never exceed the remaining locked/target amount.
- No additional release is scheduled after the wallet target is reached.
- Failed releases are retried a bounded number of times; the persisted retry count and terminal status are visible to operations.
- Pausing, cancellation, stale jobs, and duplicate job delivery must not release funds twice.
- Release destinations are wallet, main balance, or bank, as supported by the wallet configuration and payout workflow. Provider payout processing and wallet-balance releases must be represented distinctly.

**Acceptance:** A not-yet-due release is not executed. A due release is applied at most once, and its balance movement, transaction, ledger entry, status, notification, and next-release decision are consistent after retries.

### FR-7: Payouts and financial history — P0

- The system records a payout's amount, fee, net amount, destination, customer, wallet, provider references, status, timestamps, failures, and retry count.
- Customers can inspect wallet payout history and overall transaction history.
- Payouts must be processed asynchronously where provider interaction is needed, with idempotent provider references and explicit success/failure outcomes.
- A payout failure must not be presented as a completed payout. The associated funds must remain accounted for and be recoverable or reconciled according to the payout state machine.
- Every material balance change has a corresponding transaction and ledger record in the same consistent financial operation.

**Acceptance:** For a completed payout, gross amount equals fee plus net amount in the same currency. A failed or duplicate payout attempt cannot silently debit funds twice or create a false success state.

### FR-8: Optional wallet renewal — P1

- A customer can create, inspect, update, enable, or disable one renewal policy per wallet.
- A policy supports a trigger type/amount, refill amount type/value, minimum main-account balance, optional maximum renewal count, and a count of completed renewals.
- Each attempt is recorded as a renewal event with outcome, amount, reason, and associated transaction when applicable.
- Renewal must not overdraw the main balance or ignore policy limits. Failed or ineligible attempts are recorded and do not masquerade as successful refills.

**Acceptance:** A qualifying trigger creates no more than the configured refill and only when the balance floor and renewal limits permit it. Disabling a policy prevents subsequent automatic renewals.

### FR-9: Notifications and dashboard — P1

- The customer dashboard summarizes main balance and wallet state.
- The customer can read notifications individually or mark all as read.
- The system can deliver account and product notifications in-app and, where configured, by email or SMS.
- Financial state is committed before external email/SMS delivery is queued. Notification delivery failure does not reverse a valid deposit, wallet creation, release, or payout.
- User notification preferences govern applicable optional alerts.

**Acceptance:** A notification failure is retriable and observable but does not change the underlying financial transaction outcome. Read state is persisted per customer.

### FR-10: Categories, templates, and feature availability — P1

- Customers can browse wallet categories and active wallet templates.
- A template can provide default target/release amounts, frequency configuration, destination, category, tags, icon, and display order.
- Feature flags control supported feature availability and are exposed only to appropriately authorized actors.

**Acceptance:** Inactive templates are not offered for new wallets. A feature disabled by a flag cannot be newly invoked through a direct API call.

## 7. Data model and domain boundaries

The EF Core model uses PostgreSQL as the system of record. Important entities and relationships include:

| Domain area | Persisted concepts |
|---|---|
| Identity and access | Identity users and roles, OTP verifications, refresh tokens, transaction-PIN metadata |
| Funding and accounts | Customer main balance, virtual accounts, bank accounts, provider references |
| Wallet setup | Wallets, wallet categories, wallet templates, one wallet rule per wallet |
| Schedule processing | Scheduled releases linked to wallets and wallet rules |
| Accounting | Transactions and ledger entries; payout records linked to wallets and optionally bank accounts |
| Automation | Renewal policies and renewal events |
| Product operations | In-app notifications and feature flags |

The `Money` value object stores integer minor units plus a currency code; financial calculations must not use floating-point storage. Wallet amounts are represented by explicit balance fields, and the transaction/ledger history explains movements. Most domain records inherit audit timestamps and soft-delete metadata. Provider events and job processing must be idempotent, with PostgreSQL remaining authoritative for financial state; Redis is a cache/coordination aid, never the sole record of a balance or transaction.

## 8. Non-functional requirements

### Financial integrity and reliability

- Balance mutation and its corresponding transaction/ledger records are atomic.
- Provider webhooks and background jobs are idempotent and safe under retries and concurrent delivery.
- Maintain a reconciliation path across provider transaction, customer transaction, ledger, wallet, and payout states.
- Persist financial amounts in integer minor units and enforce same-currency arithmetic.
- Do not depend on Redis availability for authoritative financial correctness.

### Security and privacy

- Authenticate customer operations and enforce customer-level resource ownership on every read and write.
- Verify provider webhook signatures before accepting payment state changes.
- Protect secrets and sensitive identity/bank data; store only what product and compliance require, with hashes or provider tokens where appropriate.
- Never log access tokens, OTPs, full bank details, raw payment payloads containing personal data, or provider secrets.
- Apply rate limits and safe error responses to authentication, verification, PIN, and payment endpoints.
- Keep privileged admin operations role-gated and auditable.

### Availability and observability

- Background job failures, retries, terminal failures, webhook processing, and provider reconciliation must be observable to operations.
- Email/SMS provider outages must not block or roll back committed financial operations.
- Cache keys and invalidation must be explicit for any cached customer-facing balance, wallet, feature, or template data; stale cached values must not authorize a financial operation.
- Product-level latency, availability, recovery-time, and recovery-point targets require agreement before production launch; this repository does not establish numeric SLOs.

### Accessibility and usability

- Amounts, destination, schedule timezone, next release, and balance movements must be explicit in customer-facing screens.
- Schedule preview must communicate invalid configurations and the effects of the final partial release before confirmation.
- Customer-facing errors must explain the action needed without exposing internal implementation details.

## 9. Success measures

Instrument and report these measures before setting numerical targets:

- Registration-to-verification and verification-to-first-funding conversion.
- Funding initiation-to-success rate, separated by provider and outcome.
- Wallet creation completion rate and validation/insufficient-balance failure rate.
- On-time scheduled release success rate and terminal release failure rate.
- Payout completion rate, failure/retry rate, and time to final provider outcome.
- Financial reconciliation discrepancy count and value; the target for unexplained discrepancy is zero.
- Renewal attempt success rate and policy-limit/insufficient-balance outcomes.
- Notification delivery success and retry rates.
- Support contacts associated with funding, release, payout, and balance comprehension.

## 10. Risks and implementation clarifications

These are contract questions to resolve while converting this draft into a launch specification:

1. **API contract:** The project walkthrough documents a singular `/api/v1/wallet/...` route, while the current wallet controller uses `/api/v1/wallets/...`. Publish one versioned contract and align examples and clients.
2. **Due-release behavior:** The release job has a due-date filter commented out in [ProcessScheduledReleasesJob.cs](Mova.Infrastructure/Jobs/ProcessScheduledReleasesJob.cs), meaning it appears to process all scheduled releases regardless of actual due time. This may be intentional for a backlog/backfill job, but is ambiguous and worth clarifying.
3. **Relock-unused availability:** `RelockUnusedFunds` is referenced in the walkthrough and commented out in the controller, but the feature is not actually exposed; the endpoint is commented in [WalletController.cs](Mova.Api/Controllers/V1/WalletController.cs). That is a real product gap.
4. **Schedule completeness:** The domain enum includes an hourly frequency, while the product schedule documentation defines once through custom interval. Confirm supported types, weekday numbering, timezone source, daylight-saving behavior, invalid month dates, and what happens when a release is missed.
5. **Payout initiation and failed-payout recovery:** The schema and background job support payout processing, but the precise customer initiation and recovery contract needs to be documented, including fee disclosure, authorization/PIN requirements, reversal/refund behavior, and reconciliation.
6. **Wallet break and restart:** Define exactly how locked, available, unused, and already-paid-out funds behave for each operation, and what fees or customer confirmation apply.
7. **Eligibility and limits:** Confirm onboarding/KYC requirements, permitted customer jurisdictions, minimum/maximum funding and wallet amounts, and withdrawal restrictions with product, risk, and compliance owners.
8. **Roadmap vs. current implementation:** Redis, notification integrations, templates, renewal policies, and payout handling have code-level representations; validate their production configuration, access controls, failure handling, and supported customer-facing flows before describing them as generally available.

## 11. Source basis

This draft was derived from [PROJECT_WALKTHROUGH.md](./PROJECT_WALKTHROUGH.md), the API controllers and application commands/queries, the domain entities and enums, EF Core configurations under `Mova.Infrastructure/Persistence/Configurations`, and the existing test suite. In particular, the core data model is defined by `ApplicationDbContext` and entities including `Wallet`, `WalletRule`, `ScheduledRelease`, `Transaction`, `LedgerEntry`, `Payout`, `RenewalPolicy`, and `RenewalEvent`.
