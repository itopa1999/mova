# Backend Readiness Precheck

Run this checklist against the **same environment and database** that will serve the backend. Do not declare the backend ready until every applicable check passes.

## Automated readiness endpoint

The API exposes an anonymous deployment-probe endpoint:

```http
GET /health/ready
```

For example, using the local HTTP launch profile:

```text
http://localhost:5099/health/ready
```

- [ ] HTTP `200 OK` with overall status `Healthy` means the checks passed.
- [ ] HTTP `503 Service Unavailable` with overall status `Unhealthy` means at least one readiness check failed; inspect each check's safe message.
- [ ] The endpoint checks database connectivity and pending migrations, active banks, Redis ping and write/read round trip, deposit and payout feature gates/providers, and active wallet templates with valid categories.
- [ ] The feature-flag checks require deposits and payouts to be enabled, with at least one provider enabled for each. Only enable providers configured and operational in the environment.

The endpoint does not expose connection strings or raw exception messages. It reads feature flags directly from the database, so it verifies persisted settings rather than relying on the application cache.

## 1. Database connectivity and startup seeding

- [ ] Confirm the API starts with the intended environment configuration and can connect to PostgreSQL.
- [ ] Confirm database migrations have been applied before the API is started.
- [ ] Confirm startup completes its `DatabaseSeeder` run. Startup seeds wallet categories, missing feature flags, and missing wallet templates.
- [ ] Remember that startup seeding inserts missing feature flags only. It does **not** overwrite an existing flag's enabled state.

## 2. Banks are populated and served

The bank list is read from active rows in `banks`. The startup `DatabaseSeeder` does not populate this table; make sure the environment's bank import/sync process has run.

Run against the target database:

```sql
SELECT COUNT(*) AS active_bank_count
FROM banks
WHERE "IsActive" = TRUE
  AND "IsDeleted" = FALSE;
```

- [ ] `active_bank_count` is greater than zero.
- [ ] Call `GET /api/v1/bank-account/banks` and confirm it returns a non-empty bank list.
- [ ] If bank rows were recently imported, account for the Redis bank-list cache (up to 6 hours); invalidate the relevant `banks:` cache keys or wait for expiry before judging the endpoint response.

## 3. Redis is reachable and functioning

The API requires the `Redis:URL` setting and fails startup if it cannot establish a Redis connection. Redis is also used for application caching and transaction-PIN verification controls.

- [ ] Confirm `Redis:URL` is set in the target environment. Do not print or commit its value.
- [ ] From a host/container with the same network access as the API, connect using the configured Redis endpoint and run:

```text
PING
```

Expected response:

```text
PONG
```

- [ ] Confirm the API starts successfully and logs no Redis connection failure.
- [ ] Confirm a Redis write/read/delete round trip succeeds using an approved temporary key; remove the key after the check.

## 4. Transaction-related feature flags are enabled

Deposit initialization checks `AllowDepositFunds` and then checks the selected payment provider's flag. The integer values below come from `FeatureFlagName` and are stored in `feature_flags.Name`.

```sql
SELECT
    CASE "Name"
        WHEN 5 THEN 'AllowDepositFunds'
        WHEN 6 THEN 'DepositViaPaystack'
        WHEN 7 THEN 'DepositViaMonnify'
        WHEN 8 THEN 'DepositViaFlutterwave'
        WHEN 1 THEN 'AllowWithdrawFunds'
        WHEN 2 THEN 'PayoutsViaPaystack'
        WHEN 3 THEN 'PayoutsViaMonnify'
        WHEN 4 THEN 'PayoutsViaFlutterwave'
        ELSE 'Unknown'
    END AS flag_name,
    "IsEnabled" AS is_enabled
FROM feature_flags
ORDER BY "Name";
```

- [ ] `AllowDepositFunds` is present and enabled for deposits.
- [ ] The flag for each payment provider intended for use is present and enabled:
  - `DepositViaPaystack`
  - `DepositViaMonnify`
  - `DepositViaFlutterwave`
- [ ] `AllowWithdrawFunds` and at least one intended `PayoutsVia*` provider flag are present and enabled.
- [ ] Verify the persisted values, not only `DefaultFeatureFlags`: pre-existing database values are not changed by startup seeding.
- [ ] Verify the flags take effect through the application. Feature-flag values are cached for up to 5 minutes; invalidate `feature_flags:` keys or wait for expiry after changing them.

Only enable providers configured and operational in this environment. Enabling a flag does not verify provider credentials, callback/webhook configuration, or provider account readiness.

## 5. Wallet templates are populated and usable

Startup seeds wallet categories and default wallet templates. Templates are inserted only when their names are missing, so existing template rows are not refreshed automatically.

Run against the target database:

```sql
SELECT COUNT(*) AS active_template_count
FROM wallet_templates
WHERE "IsActive" = TRUE
  AND "IsDeleted" = FALSE;

SELECT t."Name" AS template_name, c."Name" AS category_name
FROM wallet_templates AS t
LEFT JOIN wallet_categories AS c ON c."Id" = t."CategoryId"
WHERE t."IsActive" = TRUE
  AND t."IsDeleted" = FALSE
  AND (c."Id" IS NULL OR c."IsDeleted" = TRUE);
```

- [ ] `active_template_count` is greater than zero.
- [ ] The second query returns zero rows, confirming active templates reference existing, non-deleted categories.
- [ ] Call `GET /api/v1/wallet/templates` and confirm it returns active templates.
- [ ] If categories/templates were recently seeded or changed, account for the Redis template cache (up to 12 hours); invalidate the relevant `wallet_templates:` cache key or wait for expiry before judging the endpoint response.

## Readiness decision

- [ ] Banks are present and returned by the bank-list endpoint.
- [ ] Redis passes connectivity and a write/read/delete round trip.
- [ ] Required transaction and provider flags are enabled and effective.
- [ ] Active wallet templates exist, reference valid categories, and are returned by the endpoint.
- [ ] API startup and database migration status are healthy.
- [ ] `GET /health/ready` returns HTTP `200` and reports every component check as `Healthy`.

**Backend readiness: READY only when every applicable box above is checked.** Record the environment, verification time, and any provider-specific exclusions with the release checklist.
