# MOVA Production Checklist

🔴 = blocker if unresolved

---

## 1. 🔴 Production environment
- [ ] Production DB separate from development
- [ ] Secrets/`.env` not committed to Git
- [ ] No test API keys in production
- [ ] Paystack production keys configured
- [ ] Flutterwave/Monnify production keys configured (if enabled)
- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] CORS allows only the real frontend domain (no `AllowAnyOrigin()`, no unnecessary `AllowAnyHeader()`)
- [ ] No hardcoded localhost URLs or dev credentials in source
- [ ] Swagger not exposing dangerous functionality publicly
- [ ] Server timezone explicitly configured
- [ ] DB connection string verified
- [ ] Email provider production credentials verified
- [ ] Sentry production DSN verified
- [ ] Repo grep done and every match inspected:
  ```bash
  grep -RniE "localhost|127\.0\.0\.1|password|secret|api_key|apikey|test_" .
  ```

## 2. 🔴 Authentication
- [ ] Registration, login, logout
- [ ] Refresh token and access token expiration
- [ ] Invalid credentials, disabled account, deleted account
- [ ] Concurrent requests during token refresh
- [ ] Session expiration
- [ ] Unauthorized → `401`, forbidden → `403`
- [ ] Locked/restricted account returns correct response
- [ ] Password change and password reset
- [ ] PIN creation, change, verification
- [ ] Incorrect PIN and PIN attempt limits
- [ ] Email verification (if applicable)
- [ ] `X-Platform: Web` header sent on every auth request that needs it

## 3. 🔴 Cookie security
- [ ] `HttpOnly`, `Secure`, `SameSite`, `Domain`, `Path`, expiration checked in production
- [ ] Cross-origin auth verified in a real browser (Postman doesn't count)
- [ ] Tested in Chrome, Edge, Safari (if supported), mobile browser

## 4. 🔴 Authorization (IDOR)
- [ ] User A cannot access User B's wallet IDs
- [ ] ...transaction IDs
- [ ] ...bank account IDs
- [ ] ...scheduled wallet IDs
- [ ] ...trustee/circle IDs
- [ ] ...withdrawal IDs
- [ ] ...payment references
- [ ] ...profile IDs
- [ ] Ownership enforced (e.g. `Wallet.UserId == CurrentUserId`), not just `[Authorize]`

## 5. 🔴 Money / wallet logic (funding)
- [ ] Minimum and maximum amounts
- [ ] Decimal/rounding behavior
- [ ] Duplicate callback handled
- [ ] Invalid and fake payment references/callbacks rejected
- [ ] Failed, abandoned, and successful payments
- [ ] Payment verification
- [ ] Gateway timeout and unexpected responses
- [ ] Same callback × 10 simultaneous requests = exactly ONE credit

## 6. 🔴 Scheduled releases
- [ ] Releases on correct date and time
- [ ] Multiple releases
- [ ] Missed, failed, and retried releases
- [ ] Paused wallet, broken wallet, edited schedule
- [ ] Insufficient balance
- [ ] Duplicate Hangfire execution (`ReleaseWallet()` twice pays once)
- [ ] Server restart during execution
- [ ] Hangfire job retry
- [ ] Job succeeds exactly once (idempotent)

## 7. 🔴 Withdrawal
- [ ] Valid and invalid bank account
- [ ] Account verification
- [ ] Minimum withdrawal and insufficient balance
- [ ] Gateway failure and timeout
- [ ] Duplicate withdrawal request
- [ ] Retry
- [ ] Pending, successful, failed, and reversed states
- [ ] State machine distinguishes Pending / Processing / Successful / Failed / Reversed
- [ ] No permanent deduction just because a payout request was sent

## 8. 🔴 Database
- [ ] All migrations reviewed
- [ ] Production migration tested on a copy
- [ ] Indexes, foreign keys, unique constraints reviewed
- [ ] Nullable columns and decimal precision reviewed
- [ ] Transaction isolation reviewed
- [ ] Connection pooling configured
- [ ] Backups enabled
- [ ] Backup restore actually tested

## 9. 🔴 Concurrency
- [ ] Two simultaneous ₦50,000 withdrawals on a ₦50,000 balance: one succeeds, one fails
- [ ] Balance never goes negative
- [ ] Same concurrency tests for funding, wallet creation, release, withdrawal, callbacks, bank account creation
- [ ] DB transactions/locking/idempotency used where appropriate

## 10. 🔴 Gateway security
- [ ] Paystack signature verified
- [ ] Flutterwave signature verified
- [ ] Status verified against the gateway, not trusted from the callback
- [ ] Callback endpoints don't expose secrets
- [ ] Raw payloads logged safely, no sensitive payment info logged
- [ ] Duplicate callbacks handled
- [ ] Unknown references rejected
- [ ] Amount and currency validated

## 11. 🔴 API security
- [ ] Rate limiting
- [ ] Request validation and max body size
- [ ] Brute-force protection (auth, PIN, password)
- [ ] SQL injection protection
- [ ] Mass assignment protection
- [ ] Object-level authorization
- [ ] Sensitive endpoints protected
- [ ] No stack traces in error responses
- [ ] Production exception pages disabled
- [ ] Malformed input tested deliberately

## 12. 🔴 Logging
- [ ] Every important transaction answers: who, what, when, how much, reference, previous state, new state, gateway reference, gateway response, failure reason
- [ ] Never logged: password, PIN, JWT, refresh token, full card details, private keys, API secrets
- [ ] `OperationLogger` useful for investigation without leaking data

## 13. 🔴 Hangfire
- [ ] Server starts correctly
- [ ] PostgreSQL storage works
- [ ] Dashboard protected and not publicly exposed
- [ ] Failed jobs visible
- [ ] Retry behavior understood
- [ ] Recurring jobs reviewed
- [ ] Timezone correct
- [ ] Duplicate execution handled
- [ ] Server restart tested
- [ ] DB connection failure tested

## 14. 🔴 Redis
- [ ] Production connection configured
- [ ] Authentication and TLS enabled where appropriate
- [ ] No unnecessary sensitive data stored
- [ ] TTL configured
- [ ] App behaves correctly when Redis is down
- [ ] If removing Redis: dead dependencies/config removed

## 15. 🔴 Frontend
- [ ] `npm run build` output inspected
- [ ] No localhost URLs; correct production API URL
- [ ] Correct auth config, HTTPS
- [ ] Cookies, refresh, logout, protected routes work
- [ ] 401 and 403 handling work
- [ ] Mobile responsive; iPhone and Android tested
- [ ] PWA install works (if required)
- [ ] Profile images load quickly, with fallback for broken images
- [ ] Loading, empty, and error states exist
- [ ] Removed: `console.log()`, `debugger`, temporary alerts, test buttons, fake data, dev banners

## 16. 🔴 Performance
- [ ] API response time measured
- [ ] DB query time measured
- [ ] Gateway response time measured
- [ ] Dashboard and transaction history load times measured
- [ ] Profile image: size, cache headers, response time, repeated requests, CDN behavior checked (no 4 MB images)

## 17. 🔴 CORS
- [ ] Frontend → API domains explicitly configured
- [ ] `OPTIONS` preflight verified
- [ ] `X-Platform`, `Content-Type`, `Authorization` allowed where required

## 18. 🔴 HTTPS / domain
- [ ] Frontend and backend on HTTPS
- [ ] SSL certificate valid
- [ ] HTTP redirects to HTTPS
- [ ] Cookies use `Secure`
- [ ] No mixed content
- [ ] DNS and API domain resolve correctly
- [ ] Callback URLs updated at payment providers

## 19. 🔴 Secrets
- [ ] Scanned: `.env`, `.env.*`, `appsettings*.json`, `*.pem`, `*.key`, `*.p12`, WireGuard configs
- [ ] Scanned: API keys, JWT secrets, DB passwords, SMTP passwords, payment keys
- [ ] Git history checked (deleting from the current file isn't enough)
- [ ] **Rotate the WireGuard private key and pre-shared key pasted earlier, if real and active**

## 20. 🔴 Git / repository
- [ ] `git status`, `git diff`, `git diff --cached` reviewed
- [ ] `git ls-files | grep -E '\.env|secret|key|pem'` reviewed
- [ ] No secrets, test credentials, DB dumps, production logs, or uploaded user data
- [ ] No unnecessary files
- [ ] `.gitignore` correct

## 21. 🔴 Error handling
- [ ] Users never see exception names or stack traces
- [ ] Generic message plus `request_id` returned
- [ ] `request_id` traceable in logs/Sentry
- [ ] Pattern used consistently everywhere

## 22. 🔴 Monitoring and alerts
- [ ] API availability
- [ ] 5xx rate
- [ ] 401/403 spikes
- [ ] Payment failures
- [ ] Withdrawal failures
- [ ] Hangfire failures
- [ ] DB connectivity
- [ ] CPU, RAM, disk
- [ ] Alerts configured

## 23. 🔴 Backup and disaster recovery
- [ ] Documented: backup location, frequency, retention, restore procedure
- [ ] Documented: server recovery, secret rotation, gateway config, DNS config
- [ ] Recovery actually tested
- [ ] "Server died at 2 AM" runbook exists

## 24. 🔴 Legal / user-facing
- [ ] Privacy Policy
- [ ] Terms of Service
- [ ] Cookie policy (if applicable)
- [ ] Account deletion flow
- [ ] Contact/support info
- [ ] Transaction/fee disclosure
- [ ] Payment/withdrawal terms
- [ ] Nigerian regulatory/compliance review

## 25. 🔴 Production smoke test (one controlled account)
- [ ] Register → verify → login
- [ ] Fund wallet
- [ ] Create controlled wallet and schedule
- [ ] Process release and verify balance
- [ ] Add bank account and withdraw
- [ ] Verify transaction
- [ ] Logout and login again
- [ ] Failure paths tested

## 26. 🔥 "Attack MOVA" test

### As a malicious user
- [ ] Change another user's wallet ID
- [ ] Reuse a payment callback
- [ ] Reuse a withdrawal request
- [ ] Send two withdrawals simultaneously
- [ ] Modify release amount
- [ ] Skip PIN verification
- [ ] Brute-force PIN
- [ ] Reuse expired token and old refresh token
- [ ] Call protected endpoints without auth
- [ ] Change another user's bank account

### With a broken gateway
- [ ] Timeout
- [ ] Duplicate callback
- [ ] Wrong amount
- [ ] Wrong reference
- [ ] Success response but delayed webhook
- [ ] Webhook before redirect
- [ ] Redirect without webhook

### With a broken server
- [ ] Restart during payment, withdrawal, and scheduled release
- [ ] DB temporarily unavailable
- [ ] Redis unavailable
- [ ] Hangfire restart
- [ ] Gateway unavailable

---

## Launch gate (all must be 🟢)
- [ ] Authentication: pass
- [ ] Authorization / IDOR: pass
- [ ] Wallet accounting: pass
- [ ] Payment callbacks: pass
- [ ] Withdrawal: pass
- [ ] Scheduled releases: pass
- [ ] Concurrency: pass
- [ ] Database backups: restore verified
- [ ] Secrets: clean
- [ ] CORS: locked down
- [ ] HTTPS: pass
- [ ] Monitoring: active
- [ ] Error handling: pass
- [ ] Legal pages: present
- [ ] Disaster recovery: documented
- [ ] Mobile/browser testing: pass

**One red item in money movement, authentication, authorization, secrets, or backups means don't launch.**
