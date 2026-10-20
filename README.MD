# Privacy-preserving aggregation

This project lets data partners combine statistics without sending their individual figures to the organisation collecting them. Each partner masks its figures locally. The server adds the masked submissions together, cancelling the masks and recovering the combined total.

For the Development Data Partnership, the use case is collecting AI token usage statistics across providers by country, month, and category. Partners can contribute to a shared picture of usage while keeping their own figures confidential, subject to the [privacy assumptions and limits](#privacy-assumptions-and-limits) below.

This repository contains the .NET 10 aggregation API and a shared C# library. The partner workflow is designed for a browser application, but that application's source is not included in this checkout. Developers can inspect and run the API here; running a complete partner session also requires a compatible client.

## What is shared

The **aggregator** is the server that coordinates a collection and combines submissions. It receives masked figures associated with each partner, rather than the original figures.

| Information | Who has access |
| --- | --- |
| A partner's original figures, private key, and shared secrets | The partner's client; these should stay local |
| Partner identities, the collection roster, and submission progress | The aggregator; authorised users can also access roster and status information |
| Public keys and encrypted key-exchange messages | The aggregator relays these between partners |
| Each partner's masked submissions | The aggregator stores these with partner and metric identifiers |
| Number of contributors to each metric and a fingerprint for comparing contributor groups | The aggregator computes these during the participation check; administrators can see them |
| Combined figures for eligible metrics | Available through the authenticated API after the required submissions arrive |

Participation is not anonymous. The protection concerns the submitted figures, and the totals themselves can still reveal information when combined with other knowledge.

## A collection round

A **collection round**, called an *epoch* in the code, has a fixed roster of partners and a defined set of metrics. A metric is one combination of country, month, indicator, and category (called a *segment* in the API).

1. **Register and join a round.** Partners authenticate with Microsoft Entra ID, the organisation's sign-in service. An administrator selects at least three registered partners and the starting month. Administrator accounts can observe and manage rounds but cannot contribute data.
2. **Establish shared secrets.** Each partner's client generates a key pair locally and exchanges public keys and encrypted messages through the server. Every pair of partners ends up with a shared secret used to calculate matching masks. Partners do not need a direct connection to one another or secrets agreed in advance.
3. **Confirm which metrics to contribute.** Each partner submits a masked yes/no answer for every metric. The system calls this the *quorum check*. Once everyone has answered, the server calculates how many partners will contribute to each metric. Metrics with fewer than three contributors are excluded from results.
4. **Submit masked figures.** Each partner submits a masked value for every required metric, including a masked zero where it opted out or the metric was excluded. Every partner in the round must still submit so that all masks cancel.
5. **Read the results.** The metric API returns a total once every partner has submitted that metric. The administrator's round summary waits until every partner has submitted every required metric, then the round closes.

The client must retain its private key and shared secrets through the round. If that state is lost after the exchange has started, an administrator may need to cancel and recreate the round; the server cannot recover those secrets.

The current collection schema covers five countries, four months, and ten indicator/category combinations: 200 values per partner. It is defined in code rather than configured for each round. See the [collection schema][schema] for the countries and categories.

## How the masks cancel

Suppose three partners contribute token counts for the same metric. Each pair agrees on a mask: A–B uses 15, A–C uses 8, and B–C uses 4. One partner adds the pair's mask and the other subtracts it.

| Partner | Original count | Masks applied | Submitted count |
| --- | ---: | --- | ---: |
| A | 100 | +15 +8 | 123 |
| B | 50 | −15 +4 | 39 |
| C | 20 | −8 −4 | 8 |
| **Total** | **170** | **0** | **170** |

The server receives 123, 39, and 8. Adding them gives 170 because every mask appears once with each sign. This illustrates cancellation; these small masks would not provide the protection used by the implementation.

In the implementation, partners derive masks from their shared secrets and the metric identifiers. Masks span a 64-bit integer range, and addition wraps around that range. The server uses the same arithmetic to recover the total. The pairwise secrets are established using **ML-KEM-768**, a cryptographic mechanism for establishing a shared secret using a public key and an encrypted message.

For the implementation, see [key exchange][kem] and [mask generation and summation][masks].

## Privacy assumptions and limits

The intended protection assumes that the server follows the protocol and tries to learn from the messages it receives, and that partners use a correct client and keep their local secrets private. A server that alters public keys, the roster, or client software is outside that assumption. Choosing ML-KEM for key exchange does not by itself protect the whole application against such behaviour.

- **Collusion and other knowledge can expose a contribution.** If everyone except one contributor shares their figures, they can subtract those figures from the total to recover the remaining contribution. A minimum of three contributors does not eliminate this risk.
- **Totals disclose information.** Comparing totals across overlapping groups or combining them with known figures can reveal individual values. The system returns exact totals; it does not add statistical noise to the published result.
- **Every partner is needed.** There is no dropout recovery. A missing submission prevents the matching masks from cancelling. Administrators can cancel and recreate an unfinished round.
- **The server cannot verify the underlying figures.** It validates identity, request format, and round membership, but cannot inspect a masked value to prove that it is truthful or that an opted-out partner contributed zero. Correct participation counts and fingerprints also depend on correctly formed client answers.
- **Participation metadata remains visible.** The server knows who is in a round and who has submitted. Contributor counts and group fingerprints reveal additional information about participation; a count equal to the roster size, for example, identifies the whole group as contributors.
- **Local state must remain private and available.** A compromised client can expose original figures or secrets. Losing secrets can prevent completion of a round.

## System architecture

| Component | Responsibility |
| --- | --- |
| Partner client | Holds original data and private keys, establishes shared secrets, and masks participation answers and figures locally. Browser client source is not included in this checkout. |
| ASP.NET Core API | Authenticates requests, manages rounds, relays key-exchange messages, validates submissions, and computes totals. |
| SQL Server / Azure SQL database | Stores partner records, rounds, public keys, encrypted key-exchange messages, masked answers, and masked figures. |
| Microsoft Entra ID | Supplies identity tokens and group membership used to distinguish partners and administrators. |
| Shared C# library | Defines data models, the collection schema, key-exchange and masking helpers, and participation evaluation. |

## For developers

### Follow the implementation

The [API project](AIRoundTableSecretSharingAPI/) and [shared library](AIRoundTableSecretSharingCommon/) contain the implementation available here. These code references follow the collection workflow:

| Stage | Relevant code |
| --- | --- |
| Identify partners and enforce roles | [Authorization policies][auth] and [partner registration][registration] |
| Establish pairwise secrets | [ML-KEM helper][kem], [public-key registration][keys], and [encrypted-message relay][ciphertexts] |
| Derive masks and sum submissions | [Mask generation, sign ordering, and wrap-around arithmetic][masks] |
| Count contributors and exclude metrics below quorum | [Quorum submission][quorum] and [participation evaluation][quorum-grid] |
| Accept figures and return results | [Submission gates][submission-gate], [metric results][results], and [administrator summary][admin-results] |

For each pair, the partner with the larger ID in ordinal string order establishes the shared secret for the smaller one. The smaller partner adds the derived mask and the larger subtracts it. Metric masks use HMAC-SHA256, a keyed function that both partners can evaluate from their secret, over the UTF-8 string `country|month|indicator|segment`. On a little-endian host, the C# helper interprets the first eight output bytes as a signed 64-bit integer. Clients must match the identifiers, encoding, byte order, and arithmetic. The recovered total must fit within a signed 64-bit integer.

Key-exchange requests include round and device identifiers. Participation answers contain three masked values: a yes/no flag and that flag multiplied by each of two secret random tags. The server sums the flags to count contributors and combines the tag sums into a group fingerprint. Matching fingerprints are intended to identify matching contributor groups within a round; collisions are possible. The client must use separate masks for participation quantities and metric figures. The browser implementation of this convention is not included here.

### Run the API

You need the .NET 10 SDK, a SQL Server or Azure SQL database, and Entra configuration for the API. Authenticated requests also require an Entra client that can obtain access tokens for the API, with the relevant group membership included in those tokens.

Supply these configuration values through environment variables or local application settings. Double underscores map to configuration sections in `appsettings.json`.

| Environment variable | Value |
| --- | --- |
| `AzureAd__Instance` | `https://login.microsoftonline.com/` |
| `AzureAd__TenantId` | Entra tenant ID |
| `AzureAd__ClientId` | API app registration's client ID |
| `AzureAd__AdminGroupId` | Administrator group object ID |
| `AzureAd__PartnerGroupId` | Partner group object ID |
| `ConnectionStrings__DefaultConnection` | SQL connection string |
| `Cors__AllowedOrigins` | Comma-separated browser client origins |

From the repository root:

```sh
dotnet restore AIRoundTableSecretSharingCommon.sln --configfile nuget.docker.config
dotnet build AIRoundTableSecretSharingCommon.sln --no-restore
dotnet run --project AIRoundTableSecretSharingAPI --no-launch-profile --no-restore --urls http://localhost:5149
```

The selected NuGet configuration uses nuget.org; the default `nuget.config` uses the World Bank package mirror. Startup [applies database migrations automatically][startup], so use a database whose schema this application may update. Configure credentials locally or in your hosting environment rather than committing them.

All API routes start with `/api` and require an Entra bearer token. The API does not issue tokens through `/auth/token`. In Development mode, [OpenAPI and Scalar are enabled][api-docs]; the launch profile's old `swagger` URL does not correspond to a configured Swagger UI. Development mode also [disables certificate validation for the identity metadata backchannel][development-auth]; production should use the normal validation path.

The older [SecureProducerClient][old-client] expects the removed token endpoint, omits current round/device fields, and has no participation-check step. It also logs original figures and parts of secrets. It is historical reference rather than a supported client for real submissions. [TESTCASES.md](TESTCASES.md) likewise describes earlier request formats and expected outcomes, rather than executed test results. The solution currently has no automated test project.

The implementation links above are pinned to a published commit so the referenced code stays stable. Update them when the documented implementation changes.

[kem]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingCommon/Core/MlKemKeyExchange.cs#L18-L43
[masks]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingCommon/Core/SecureNoiseGenerator.cs#L19-L64
[schema]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingCommon/Models/EpochGrid.cs#L9-L31
[auth]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Program.cs#L35-L49
[registration]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Controllers/RegistryController.cs#L175-L211
[keys]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Controllers/KeyExchangeController.cs#L67-L117
[ciphertexts]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Controllers/CiphertextController.cs#L37-L109
[quorum]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Controllers/QuorumController.cs#L43-L145
[quorum-grid]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingCommon/Models/QuorumGrid.cs#L17-L59
[submission-gate]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Controllers/MetricsController.cs#L330-L373
[results]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Controllers/MetricsController.cs#L210-L301
[admin-results]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Controllers/AdminController.cs#L407-L447
[startup]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Program.cs#L98-L115
[api-docs]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Program.cs#L117-L121
[development-auth]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingAPI/Program.cs#L60-L68
[old-client]: https://github.com/datapartnership/AIRoundTableSecretSharing/blob/c6384e494cb5477663f82a41e4cfcef99721272c/AIRoundTableSecretSharingCommon/Producers/SecureProducerClient.cs#L40-L65
