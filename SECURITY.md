# ApexPost API security (Week 4, Part D)

All data in this repository is fictional. No secret, key or certificate is committed: the JWT signing key and the partner API keys come from **user-secrets** or **environment variables** only.

## 1. What is protected and how

| Caller | Credential | Used on |
| --- | --- | --- |
| Courier mobile app / staff tools | **JWT bearer access token** (`Authorization: Bearer ...`) | `/api/v1/*`, `/api/v2/*` |
| Partner systems (server to server) | **API key** (`X-Api-Key` header) | `GET /api/v2/partner/shipments/{trackingNumber}` only |
| Developers | `POST /dev/token` (Development only) | Gets a test JWT |

* **Authentication**: `AddAuthentication().AddJwtBearer()`. The API validates issuer, audience, lifetime and signing key (HS256, key >= 32 characters) on every request. Clock skew is 30 seconds. `Jwt:Issuer` and `Jwt:Audience` are in `appsettings.json`; `Jwt:SigningKey` is a secret. **The API fails fast at start-up** with a clear message if the key is missing or too short.
* **Claims**: `sub` and `courierId` (the courier's id, e.g. `C-1001`), `role` (`Courier` or `Admin`). `role` is configured as the role claim type and inbound claim mapping is off, so the names stay exactly as issued.
* **Authorization** (all in attributes and one filter, controllers stay thin):

| Operation | Rule |
| --- | --- |
| Upload scans, change shipment status | role `Courier` |
| Delete a shipment | role `Admin` |
| Read shipments and scan history, create, replace | any signed-in user |
| Upload scans | additionally, any `courierId` in a scan must equal the token's `courierId` (`ScanOwnershipFilter`) |
| `/dev/token`, `/openapi`, `/swagger`, partner route | anonymous at the JWT level (the partner route has its own key check) |
| Everything else | **fallback policy: authenticated user required** (secure by default; a new endpoint is protected until someone opts out) |

* **401 vs 403**: 401 = we do not know who you are (no, invalid or expired token). 403 = we know you, but you may not do this (wrong role, or a scan for another courier). Both are `application/problem+json` with `traceId` and `correlationId`, written through `IProblemDetailsService` from `JwtBearerEvents.OnChallenge` / `OnForbidden`, so they look like every other API error. The 401 carries `WWW-Authenticate: Bearer`.
* **Scan ownership**: `ScanItemRequest` has an optional `courierId`. If present and different from the token's claim, the whole upload is refused with 403 (`errorCode: COURIER_MISMATCH`) and nothing is stored. If omitted, the scan is taken to belong to the caller. A courier token with no `courierId` claim is refused too.
* **Partner API key**: `PartnerApiKeyAttribute` (an authorization filter) reads `Partner:ApiKeys` (an array). Each configured key and the supplied key are hashed with SHA-256 and compared with `CryptographicOperations.FixedTimeEquals`; every key is checked, there is no early exit, so timing reveals neither key content nor which key matched. Missing or wrong key: 401. **No key configured: 500** (a deployment error must never fail open or look like a bad key). This mirrors the Week 2 `ApiKeyValidationMiddleware`.
* **OpenAPI**: the documents declare an HTTP bearer (JWT) scheme, and in v2 an API-key scheme for the partner route. Operations that need a token list the `Bearer` requirement; Swagger UI shows **Authorize** and keeps the token across reloads (`EnablePersistAuthorization`).

## 2. Secrets (PowerShell, from the repo root)

The values below are obviously fictional placeholders. Generate your own random strings.

```powershell
dotnet user-secrets set "Jwt:SigningKey"    "REPLACE-WITH-AT-LEAST-32-RANDOM-CHARACTERS" --project .\ApexPost.Api
dotnet user-secrets set "Partner:ApiKeys:0" "replace-with-a-long-random-partner-key"     --project .\ApexPost.Api
# a second key lets you rotate without downtime:
dotnet user-secrets set "Partner:ApiKeys:1" "replace-with-another-random-partner-key"    --project .\ApexPost.Api
```

Environment variables work too (`Jwt__SigningKey`, `Partner__ApiKeys__0`), which is what a deployment would use (or a key vault).

## 3. OAuth 2.0 background: how this maps to a real identity provider

`/dev/token` is a stand-in. In production the API never issues tokens; an **identity provider (IdP)** does.

* **Courier app: Authorization Code flow with PKCE.** The app is a *public client* (it cannot keep a secret; anyone can unpack an app). It opens the system browser to the IdP, the courier signs in there (the app never sees the password), the IdP returns a one-time *authorization code* to the app's redirect URI, and the app swaps the code for tokens together with a random **code verifier**. PKCE (the verifier and its hashed *code challenge*) stops a malicious app that intercepted the code from redeeming it.
* **Partner systems: Client Credentials flow.** There is no human. The partner's server authenticates to the IdP with its own client id and secret (or a certificate) and receives an access token scoped to the partner API.
* **Roles of the tokens and parties**
  * *Identity provider*: authenticates the user or client, owns the signing keys, issues tokens, can revoke access.
  * *Access token (JWT)*: short-lived (minutes to an hour), sent to the API on every call, carries claims (`sub`, `role`, `courierId`, `aud`, `exp`). The API only validates it; it never calls the IdP per request (signature check with the IdP's public key).
  * *Refresh token*: long-lived, kept in secure storage on the device, sent only to the IdP to obtain a new access token. It lets the access token be short-lived without forcing the courier to sign in again. It can be revoked and rotated.
* **Why API keys are weaker than OAuth2 tokens**: a key is a long-lived static bearer secret; it identifies no user, has no expiry, no scopes, no audience, cannot be narrowed or revoked per session, and once leaked works until someone notices and rotates it. OAuth2 tokens are short-lived, scoped, bound to an audience, issued per client or user and revocable. We use a key only for the one read-only partner route, with constant-time comparison and rotation support; a real partner integration should move to Client Credentials.

### Moving to a real IdP (checklist)

1. Replace the symmetric `Jwt:SigningKey` validation with `options.Authority = "https://<idp>/"` (metadata discovery downloads the IdP's public signing keys and rotates them automatically) and keep `ValidAudience` set to this API.
2. **Delete `/dev/token`** and `ITokenService` / `JwtTokenService` (or leave the endpoint unmapped; it already exists only in Development).
3. Define **scopes** (for example `shipments.read`, `scans.write`, `partner.read`) and the audience for this API at the IdP, and authorize on scopes or on the IdP's role claim (map it with `RoleClaimType`).
4. Rotate keys: the IdP publishes several keys at once; the API picks the one named in the token's `kid`. For the partner API keys, add the new key as `Partner:ApiKeys:1`, move the partner over, then remove the old one.
5. Keep `courierId` as a claim issued by the IdP (a custom claim or mapped from the user profile); the ownership check does not change.
6. Remove `Jwt:SigningKey` from configuration.

## 4. Transport security: why HTTPS is not optional

A bearer token and an API key are *possession* credentials: whoever holds the string is the user. Over plain HTTP they cross every Wi-Fi hotspot, proxy and router in clear text, so anyone on the path can copy them and replay them (the signature on a JWT proves who issued it, not who is sending it). Therefore:

* `UseHttpsRedirection()` runs in every environment, before authentication, so an `http://` request is redirected before any credential is looked at.
* `UseHsts()` runs outside Development: browsers then refuse plain HTTP to this host for a year (Development is excluded so a localhost HSTS entry does not stick in your browser).
* Production traffic should terminate TLS 1.2+ with a certificate from a public CA. Never put tokens or keys in URLs or logs.

### The mobile app's DEBUG-only localhost certificate bypass

On a developer machine the API runs on `https://localhost:7153` with a self-signed ASP.NET **development certificate**, which an Android emulator does not trust. The MAUI app therefore relaxes certificate validation **only** for the dev host, and **only** inside `#if DEBUG`: the code is not compiled into Release builds, so a shipped app cannot be tricked into accepting a forged certificate (which would let a man-in-the-middle read the bearer token). The bypass also checks the host name (localhost / emulator loopback), never "accept everything". Release builds use the normal system trust store. On Windows, `dotnet dev-certs https --trust` removes the need for any bypass.

## 5. Out of scope here (known limits)

* `/dev/token` hands tokens to anybody on a Development machine by design. Never deploy with `ASPNETCORE_ENVIRONMENT=Development`.
* Symmetric HS256 means the API and the issuer share the secret; an IdP with asymmetric keys (RS256/ES256) is the production answer.
* No refresh-token handling or rate limiting on the Part D API (the Week 2/3 MVC app has rate limiting).
