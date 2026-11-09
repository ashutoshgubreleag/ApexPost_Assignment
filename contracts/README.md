# contracts

The reviewed OpenAPI contract of each API version. Both files are produced by the running API, never written by hand:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\export-contracts.ps1
```

| File | Version | Status |
| --- | --- | --- |
| `v1.json` | 1.0 | Deprecated. Frozen: it changes only for a security fix |
| `v2.json` | 2.0 | Current. The NSwag SDK `ApexPost.Client` is generated from this file |

A pull request that changes the API must change these files too, so the contract change is visible in review.
