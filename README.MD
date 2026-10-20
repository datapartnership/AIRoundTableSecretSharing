# Privacy-Preserving Secure Aggregation

This project enables the Development Data Partnership to aggregate AI token usage figures across providers by country, month, and category while protecting each partner's individual contribution, subject to the [privacy assumptions and limits](#privacy-assumptions-and-limits) below. Partners mask their figures locally before submission, and the aggregator will be able to recover the total figures for each cell by summing the masked submissions.

The implementation uses .NET 10 and React, with pairwise mask cancellation and ML-KEM-768 for establishing shared secrets. Partners authenticate through Microsoft Entra ID and complete the protocol in their browser, without needing pre-shared secrets or direct connections to other partners.

## What is shared

Each partner retains its original figures, private key, and shared secrets locally. Its client uses those secrets to mask participation answers and data contributions before submission.

The aggregator is the entity responsible for coordinating collection and combining contributions. It receives masked submissions alongside partner and cell identifiers. It knows which partners belong to a collection round and whether they have submitted, but the protocol is designed to protect their individual figures.

Once the required submissions arrive, the aggregator calculates results for eligible cells. The quorum check identifies cells with at least three contributing partners. It also allows the Partnership to detect changes in the group of contributors between reporting periods or indicators, without revealing which partners joined or left. This information helps the Partnership interpret rankings and growth measures.

## A collection round

A collection round, also called an *epoch*, has a fixed roster of partners and a defined set of cells. Each cell identifies a combination of country, month, indicator, and category.

1. **Register and join a round.** Partners authenticate with Microsoft Entra ID, the organisation's sign-in service. An administrator selects at least three registered partners and the starting month. Administrator accounts can observe and manage rounds but cannot contribute data.
2. **Establish shared secrets.** Each partner's client generates a key pair locally and exchanges public keys and encrypted messages through the aggregation service. Every pair of partners ends up with a shared secret used to calculate matching masks.
3. **Complete the quorum check.** Before sharing usage figures, each partner declares which cells it intends to contribute to. The aggregator combines the masked answers to determine which cells meet the minimum of three contributors.
4. **Submit masked figures.** Partners contribute data only for cells they selected during the quorum check and that meet the minimum threshold.
5. **Read the results.** The aggregator combines the required submissions to calculate results for eligible cells.

The client must retain its private key and shared secrets through the round. If that state is lost after the exchange has started, an administrator may need to cancel and recreate the round. The aggregation service cannot recover those secrets.

## Quorum check

The quorum check establishes which cells have enough contributors to be included in the results. It is designed to allow partners to contribute in some countries or categories and opt out of others without directly disclosing those choices to the Partnership.

Before submitting usage figures, every partner provides an answer for each cell: `1` if it intends to contribute and `0` if it does not. The client masks these answers before sending them to the Partnership, which acts as the aggregator. Once all partners have answered, their masks cancel in the sum, giving the contributor count for each cell. Cells with fewer than three contributors are excluded.

The check also identifies changes in the group of contributors between reporting periods or indicators, such as country, industry classification, model region of origin, and model access type. The Partnership learns that the group changed, without learning which partners joined or left. A change in the results may therefore reflect a different group of contributors rather than a change in usage, which matters when calculating rankings or growth measures.

After the check, partners contribute figures only for the eligible cells they selected. Eligibility does not require a partner to contribute to a cell it opted out of.

These results do not guarantee that participation choices remain secret in every situation. For example, a count equal to the full roster reveals that every partner contributes to that cell. The [privacy assumptions and limits](#privacy-assumptions-and-limits) also apply to these outputs.

## How the masks cancel

Suppose three partners contribute token counts for the same cell. Each pair agrees on a mask: A–B uses 15, A–C uses 8, and B–C uses 4. One partner adds the pair's mask and the other subtracts it.

| Partner | Original count | Masks applied | Submitted count |
| --- | ---: | --- | ---: |
| A | 100 | +15 +8 | 123 |
| B | 50 | −15 +4 | 39 |
| C | 20 | −8 −4 | 8 |
| **Total** | **170** | **0** | **170** |

The aggregator receives 123, 39, and 8. Adding them gives 170 because every mask appears once with each sign. These small masks illustrate cancellation. The implementation uses much larger masks to protect individual contributions.

In the implementation, partners derive masks from their shared secrets and the cell identifiers. Masks span a 64-bit integer range, and addition wraps around that range. The aggregation service uses the same arithmetic to recover the total. The pairwise secrets are established using **ML-KEM-768**, a cryptographic mechanism for establishing a shared secret using a public key and an encrypted message.

For the implementation, see [key exchange][kem] and [mask generation and summation][masks].

## Privacy assumptions and limits

The intended protection assumes that the aggregator follows the protocol, even if it tries to learn individual figures from the messages it receives. Partners must also use a correct client and keep their local secrets private. An aggregator that alters public keys, the roster, or client software is outside that assumption. Choosing ML-KEM for key exchange does not by itself protect the whole application against such behaviour.

- **Collusion and other knowledge can expose a contribution.** If everyone except one contributor shares their figures, they can subtract those figures from the total to recover the remaining contribution. A minimum of three contributors does not eliminate this risk.
- **Totals disclose information.** Comparing totals across overlapping groups or combining them with known figures can reveal individual values. The system returns exact totals. It does not add statistical noise to the published result.
- **Required submissions must arrive.** There is no dropout recovery. If a partner fails to complete a required contribution, its matching masks cannot cancel. Administrators can cancel and recreate an unfinished round.
- **Masking does not establish accuracy.** The API validates identity, request format, and round membership, but cannot inspect a masked value to prove that the underlying figure is truthful. Correct quorum results also depend on correctly formed client answers.
- **Participation metadata remains visible.** The aggregator knows who is in a round and who has submitted. Quorum results can reveal additional information about participation, as described in the quorum check.
- **Local state must remain private and available.** A compromised client can expose original figures or secrets. Losing secrets can prevent completion of a round.

## System architecture

| Component | Responsibility |
| --- | --- |
| Partner client | Holds original data and private keys, establishes shared secrets, and masks participation answers and figures locally. |
| ASP.NET Core API | Authenticates requests, manages rounds, relays key-exchange messages, validates submissions, and computes totals. |
| SQL Server / Azure SQL database | Stores partner records, rounds, public keys, encrypted key-exchange messages, masked answers, and masked figures on behalf of the aggregator. |
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

Key-exchange requests include round and device identifiers. Participation answers contain three masked values: a yes/no flag and that flag multiplied by each of two secret random tags. The aggregation service sums the flags to count contributors and combines the tag sums into a group fingerprint. Matching fingerprints are intended to identify matching contributor groups within a round, although collisions are possible. Comparing fingerprints across rounds would require a consistent tagging convention that the current code does not establish. The client must use separate masks for participation quantities and usage figures.

### Prerequisites

- .NET 10 SDK
- Node.js 20+ for the React web UI
- Access to an Azure SQL database
- A Microsoft Entra ID tenant with API and web UI app registrations, an administrator security group, and a partner security group

Configure the API in `AIRoundTableSecretSharingAPI/appsettings.json` or through the environment variables below. Environment variables override the corresponding application settings.

| Application setting | Environment variable |
| --- | --- |
| `AzureAd:Instance` | `AzureAd__Instance` |
| `AzureAd:TenantId` | `AzureAd__TenantId` |
| `AzureAd:ClientId` | `AzureAd__ClientId` |
| `AzureAd:AdminGroupId` | `AzureAd__AdminGroupId` |
| `AzureAd:PartnerGroupId` | `AzureAd__PartnerGroupId` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins` |

Use `https://login.microsoftonline.com/` for `AzureAd:Instance`, the tenant and API app IDs for their respective settings, and the security groups' object IDs for the group settings. `Cors:AllowedOrigins` accepts a comma-separated list of browser client origins. Keep credentials in local configuration or hosting settings rather than committing them.

The web UI uses its own app registration through `VITE_MSAL_CLIENT_ID`. Its `VITE_ADMIN_GROUP_ID` and `VITE_PARTNER_GROUP_ID` values should match the API's group IDs.

### Run the API

```sh
cd AIRoundTableSecretSharingAPI
dotnet run
```

The API runs on `http://localhost:5149`. It [applies Entity Framework Core database migrations automatically at startup][startup].

### Implementation notes

The participation workflow above describes the intended behaviour. The current [metric result checks][results] still expect submissions from the full round roster for each cell, and the [administrator summary][admin-results] waits for the full cell grid. These checks need to be aligned with the selected-cell participation rule. They are an implementation bug, rather than a requirement for partners to contribute to unselected cells.

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
