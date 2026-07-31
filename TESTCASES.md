# E2E Test Cases — Privacy-Preserving Secure Aggregation

---

## Test Environment Assumptions

- API running at `http://localhost:5149` (or staging base URL)
- Three partners configured: `partnerA`, `partnerB`, `partnerC`
- Client credentials available per `appsettings.json`
- All tests use OAuth 2.0 Client Credentials flow to obtain Bearer tokens
- Test country: `USA`, test month: `2026-05-01`

---

## 1. Authentication

### TC-AUTH-01 — Successful token acquisition
**Given** valid `client_id` and `client_secret` for partnerA  
**When** `POST /auth/token` is called with `grant_type=client_credentials`  
**Then** response is `200 OK` with a JSON body containing `access_token`, `token_type: bearer`, and `expires_in`

### TC-AUTH-02 — Invalid client secret rejected
**Given** valid `client_id` but incorrect `client_secret`  
**When** `POST /auth/token` is called  
**Then** response is `401 Unauthorized`

### TC-AUTH-03 — Unknown client rejected
**Given** a `client_id` that does not exist in configuration  
**When** `POST /auth/token` is called  
**Then** response is `401 Unauthorized`

### TC-AUTH-04 — Missing grant type rejected
**Given** valid credentials but no `grant_type` field  
**When** `POST /auth/token` is called  
**Then** response is `400 Bad Request`

### TC-AUTH-05 — Expired token rejected
**Given** a previously valid token that has passed its `expires_in` window (60 min)  
**When** any protected `/api/` endpoint is called with the expired token  
**Then** response is `401 Unauthorized`

### TC-AUTH-06 — Missing Bearer token rejected
**Given** no `Authorization` header  
**When** any protected `/api/` endpoint is called  
**Then** response is `401 Unauthorized`

### TC-AUTH-07 — Malformed Bearer token rejected
**Given** an `Authorization: Bearer <garbage_string>` header  
**When** any protected `/api/` endpoint is called  
**Then** response is `401 Unauthorized`

---

## 2. Phase 1 — ML-KEM Key Registration

### TC-KEY-01 — Successful key registration
**Given** a valid Bearer token for partnerA and a freshly generated ML-KEM-768 encapsulation key (1184 bytes, base64-encoded)  
**When** `POST /api/keyexchange/register` is called with `{ "producerId": "partnerA", "publicKeyBase64": "<key>" }`  
**Then** response is `200 OK` or `201 Created`

### TC-KEY-02 — All three partners register keys
**Given** valid tokens for partnerA, partnerB, and partnerC  
**When** each partner registers their encapsulation key in sequence  
**Then** `GET /api/keyexchange/status` returns all three partners as registered

### TC-KEY-03 — Retrieve all encapsulation keys
**Given** all three partners have registered keys and a valid Bearer token  
**When** `GET /api/keyexchange/keys` is called  
**Then** response contains three entries, each with a valid 1184-byte base64 encapsulation key

### TC-KEY-04 — Retrieve a specific partner's key
**Given** partnerA has registered their key  
**When** `GET /api/keyexchange/keys/partnerA` is called  
**Then** response contains exactly partnerA's encapsulation key

### TC-KEY-05 — Unknown partner key returns 404
**When** `GET /api/keyexchange/keys/partnerX` is called for a non-existent partner  
**Then** response is `404 Not Found`

### TC-KEY-06 — Registration without token rejected
**When** `POST /api/keyexchange/register` is called without a Bearer token  
**Then** response is `401 Unauthorized`

### TC-KEY-07 — Invalid key size rejected
**Given** a base64 payload that does not decode to exactly 1184 bytes  
**When** `POST /api/keyexchange/register` is called  
**Then** response is `400 Bad Request`

---

## 3. Phase 2 — Ciphertext Relay (Encapsulation)

### TC-CT-01 — PartnerB posts ciphertext for partnerA
**Given** partnerB has fetched partnerA's encapsulation key and run `Encapsulate(pk_A)` locally, producing a 1088-byte ciphertext  
**When** `POST /api/ciphertext` is called with `{ "senderId": "partnerB", "recipientId": "partnerA", "ciphertextBase64": "<ct>" }`  
**Then** response is `200 OK` or `201 Created`

### TC-CT-02 — All required ciphertexts posted (3-partner set)
**Given** partnerB encapsulates for partnerA, partnerC encapsulates for partnerA and partnerB  
**When** all three ciphertexts are posted  
**Then** `GET /api/ciphertext?recipientId=partnerA` returns two ciphertexts (from B and C); `GET /api/ciphertext?recipientId=partnerB` returns one (from C)

### TC-CT-03 — Ciphertext with invalid size rejected
**Given** a base64 payload that does not decode to 1088 bytes  
**When** `POST /api/ciphertext` is called  
**Then** response is `400 Bad Request`

### TC-CT-04 — Ciphertext posted without token rejected
**When** `POST /api/ciphertext` is called without a Bearer token  
**Then** response is `401 Unauthorized`

### TC-CT-05 — PartnerA has no ciphertexts to post (smallest partner)
**Given** the alphabetical ordering convention (smaller partner never encapsulates)  
**When** `GET /api/ciphertext?recipientId=partnerB` is checked after only partnerA has acted  
**Then** no ciphertexts from partnerA are present

---

## 4. Phase 3 — Decapsulation (Local Verification)

> Decapsulation is performed locally by each partner using their private key. These tests verify that the correct ciphertexts are retrievable and that the resulting shared secrets are consistent across both sides of each pair.

### TC-DEC-01 — PartnerA retrieves ciphertexts addressed to it
**Given** ciphertexts from partnerB and partnerC have been posted  
**When** `GET /api/ciphertext?recipientId=partnerA` is called with partnerA's token  
**Then** response contains exactly two ciphertexts: one from partnerB, one from partnerC

### TC-DEC-02 — Shared secret consistency (partnerA ↔ partnerB)
**Given** partnerB ran `Encapsulate(pk_A)` → `(ct_BA, secret_AB)` and posted `ct_BA`  
**And** partnerA ran `Decapsulate(ct_BA, sk_A)` → `secret_AB_recovered`  
**Then** `secret_AB == secret_AB_recovered` (both sides independently compute the same 32-byte secret)

### TC-DEC-03 — Shared secret consistency (partnerA ↔ partnerC)
**Same as TC-DEC-02** but for the partnerC → partnerA pair

### TC-DEC-04 — Shared secret consistency (partnerB ↔ partnerC)
**Same as TC-DEC-02** but for the partnerC → partnerB pair

### TC-DEC-05 — Tampered ciphertext fails decapsulation
**Given** a valid ciphertext with one byte altered  
**When** the partner attempts `Decapsulate(tampered_ct, sk)`  
**Then** decapsulation produces a random/incorrect secret (ML-KEM IND-CCA2 property — it does not throw, but the secret will not match)  
**And** the resulting HMAC noise will be incorrect, causing the aggregate to not equal the true sum

---

## 5. Phase 4 — Masked Metric Submission

### TC-SUB-01 — Successful masked submission by partnerA
**Given** partnerA has computed its masked MAU value using the full noise protocol  
**When** `POST /api/metrics/submit` is called with `{ "producerId": "partnerA", "country": "USA", "month": "2026-05-01", "value": 1230000, "epochId": 1 }`  
**Then** response is `200 OK` or `201 Created`

### TC-SUB-02 — All three partners submit successfully
**Given** partnerA, partnerB, and partnerC each submit their masked values  
**When** all three submissions complete  
**Then** each returns a success response

### TC-SUB-03 — Submission without token rejected
**When** `POST /api/metrics/submit` is called without a Bearer token  
**Then** response is `401 Unauthorized`

### TC-SUB-04 — Duplicate submission for same partner/country/month
**Given** partnerA has already submitted for `USA / 2026-05-01`  
**When** partnerA submits again for the same country and month  
**Then** response is either `409 Conflict` or the submission is idempotently accepted (behaviour should be explicitly defined)

### TC-SUB-05 — Submission for unknown epoch rejected
**When** a submission references an `epochId` that does not exist  
**Then** response is `400 Bad Request` or `404 Not Found`

---

## 6. Aggregation

### TC-AGG-01 — Aggregate unavailable until all partners submit
**Given** only partnerA and partnerB have submitted (partnerC has not)  
**When** `GET /api/metrics/aggregate?country=USA&month=2026-05-01` is called  
**Then** response is either `202 Accepted` (pending) or `404 Not Found` — the aggregate value must not be returned prematurely

### TC-AGG-02 — Correct aggregate after all submissions
**Given** partnerA submits `1,230,000`, partnerB submits `395,000`, partnerC submits `75,000`  
**And** all masked values are derived from the noise protocol with actual values `1,000,000 + 500,000 + 200,000 = 1,700,000`  
**When** `GET /api/metrics/aggregate?country=USA&month=2026-05-01` is called  
**Then** response contains `total: 1700000` — noise has cancelled perfectly

### TC-AGG-03 — Aggregate is exact (noise cancellation verification)
**Given** the same scenario as TC-AGG-02  
**Then** the aggregate must equal exactly `V_A + V_B + V_C` with zero residual noise — any deviation indicates a bug in noise sign assignment or HMAC derivation

### TC-AGG-04 — Aggregate is not retrievable by unauthenticated caller
**When** `GET /api/metrics/aggregate` is called without a Bearer token  
**Then** response is `401 Unauthorized`

### TC-AGG-05 — Aggregate for non-existent country/month returns 404
**When** `GET /api/metrics/aggregate?country=XYZ&month=2099-01-01` is called  
**Then** response is `404 Not Found`

### TC-AGG-06 — Aggregate for different country is independent
**Given** submissions exist for `USA / 2026-05-01`  
**When** `GET /api/metrics/aggregate?country=GBR&month=2026-05-01` is called  
**Then** response is `404 Not Found` or empty — data from one country does not bleed into another

---

## 7. Registry & Epoch Management

### TC-REG-01 — List active partners
**When** `GET /api/registry/producers` is called with a valid token  
**Then** response lists all registered partners for the current epoch

### TC-REG-02 — Get current epoch info
**When** `GET /api/registry/epoch` is called  
**Then** response includes the current `epochId` and the list of partner IDs in that epoch

### TC-REG-03 — Epoch enforces complete submission set
**Given** the current epoch includes partnerA, partnerB, and partnerC  
**Then** the aggregate must not be returned until all three have submitted (see TC-AGG-01)

---

## 8. Full Happy Path — End-to-End Flow

### TC-E2E-01 — Complete three-partner aggregation cycle

This test executes the full protocol in sequence and validates the final aggregate.

| Step | Action | Expected Result |
|---|---|---|
| 1 | partnerA, B, C each obtain Bearer tokens | Three valid JWTs returned |
| 2 | Each partner generates ML-KEM-768 key pair locally | Key pairs created in memory |
| 3 | Each partner registers encapsulation key | `POST /api/keyexchange/register` → 200 for all three |
| 4 | Confirm all keys registered | `GET /api/keyexchange/status` → all three present |
| 5 | B fetches pk_A; encapsulates → posts ct_BA | `POST /api/ciphertext` → 200 |
| 6 | C fetches pk_A, pk_B; encapsulates → posts ct_CA, ct_CB | `POST /api/ciphertext` → 200 for both |
| 7 | A fetches ct_BA, ct_CA; decapsulates → derives secret_AB, secret_AC locally | Secrets match those held by B and C |
| 8 | B fetches ct_CB; decapsulates → derives secret_BC locally | Secret matches C's copy |
| 9 | Each partner computes masked MAU using HMAC noise | Masked values computed locally |
| 10 | Each partner submits masked MAU | `POST /api/metrics/submit` → 200 for all three |
| 11 | Retrieve aggregate | `GET /api/metrics/aggregate` → `total = 1,700,000` |

**Pass criteria:** The aggregate equals the sum of actual values exactly. No individual value is exposed in any API response at any step.

---

## 9. Security & Negative Cases

### TC-SEC-01 — Aggregator cannot infer individual values from stored data
**Given** the aggregator holds `pk_A, pk_B, pk_C`, `ct_BA, ct_CA, ct_CB`, and masked MAU values  
**Then** no combination of these values allows derivation of any shared secret or individual MAU (verified by design; document as a cryptographic assumption test)

### TC-SEC-02 — Partner cannot submit on behalf of another partner
**Given** partnerA's Bearer token  
**When** `POST /api/metrics/submit` is called with `"producerId": "partnerB"`  
**Then** response is `403 Forbidden` (the API should validate that the token subject matches the producerId)

### TC-SEC-03 — N−1 collusion scenario (informational)
**Given** partnerB and partnerC share their decapsulation keys and all secrets  
**Then** they can compute all noise values involving partnerA and deduce partnerA's actual value from the aggregate — this is an inherent limitation of pairwise noise, not an implementation bug; document as a known limitation

### TC-SEC-04 — Dropout scenario — partner posts ciphertext but does not submit
**Given** partnerC posts ciphertexts in Phase 2 but never submits in Phase 4  
**Then** the aggregate is never returned (all-or-nothing design) and partnerA's noise from the partnerA↔C pair does not cancel — the system should not return a partial or incorrect aggregate

### TC-SEC-05 — Replay attack — reusing a valid token from a previous session
**Given** a captured valid JWT that has not yet expired  
**When** it is replayed to submit a duplicate metric  
**Then** the system either rejects the duplicate (idempotency check) or accepts it idempotently without double-counting

---

*This document was generated by mAI.*
