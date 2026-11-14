# Client policy

[Chinese](client-policy.zh.md)

Game definitions, client build profiles and server destinations use the same
`ClientPolicy` format 1. There is no separate package or build recommendation
schema. The Launcher refuses unknown policy fields and rule types.

For local `game.toml`, `builds/<build>.toml` and store-distributed build profiles,
declare root-level tables after the other TOML sections:

```toml
[[client_policy_sources]]
name = "managed-store"
kind = "http"
base_url = "https://store.example.invalid/"
catalog_path = "catalog.toml"

[[client_policy_rules]]
plugin = "example.component"
id = "install-offer"
type = "recommend"
builds = ["kr-1256"]
sources = ["managed-store"]
default_selected = false

[[client_policy_rules]]
plugin = "example.component"
id = "minimum"
type = "min_version"
builds = ["kr-1256"]
version = "1.2.0"
```

Each rule needs a plugin identity, a stable rule ID and a type. `builds` contains
exact build IDs; an empty or omitted list applies to every build in that game.
Branch selectors and wildcards are not policy scopes.

| Type | Additional field | Effect |
| --- | --- | --- |
| `require` | None | The package must be installed and enabled before launch. |
| `disable` | None | Disable it in the launch snapshot without modifying the saved selection. |
| `recommend` | `sources`, optional `default_selected` | Offer it once per client and rule; an empty source list permits configured stores. |
| `min_version` | `version` | An enabled selection must meet this SemVer minimum. |
| `max_version` | `version` | An enabled selection must not exceed this SemVer maximum. |
| `latest` | None | Resolve the newest compatible release for an enabled selection before launch. |

Any rule may also set `languages`, a list of launcher language tags (`zh-CN`, or `zh` for every Chinese
variant). Such a rule applies only while the launcher shows one of those languages; for any other language it does
not apply, and the plugin can still be installed and enabled. For example, the builds recommend the Chinese font
plugin only to Chinese launchers. The server JSON field is also `languages`.

Recommendations always remain visible in the offer list. `default_selected`
defaults to `false` when omitted; only explicit `true` initially checks the card.
The player may change either optional choice. The server JSON field is
`defaultSelected`. Other rule types reject this field. When applicable local and
server recommendations combine, every recommendation must opt in for the merged
card to start checked. Initial installation and first-run offers use the same rule.

A source declaration does not silently add a store. Installation uses stores the
player has enabled and matches named sources by transport, address and catalog.
Version limits apply to installed candidates and store downloads. Package source
ownership (`game.plugin_sources` and `update_source`) remains separate from the
activation decision. `[plugins].default` and `game.default_plugins` select initial
components; they do not require, recommend, disable or impose version limits.

Game rules are combined with local or store-provided build rules. Destination
rules add constraints for that launch: the stricter minimum and maximum apply,
requirements accumulate, and named source restrictions intersect. A destination
cannot relax local requirements. Contradictory disable rules, an empty source
intersection, inverted version bounds, or `latest` with a maximum fail explicitly.
For the same plugin, `require` and `recommend` conflict on overlapping build scopes, including after combining game, build and destination policies. Disjoint scopes may use different choices. Within one declaration, duplicate types on overlapping scopes are also rejected.

The native runtime reads the managed session's `required` flag. Plugin manifests
cannot impose activation policy. `required_on`, `warn_on`, `require_latest`,
`game.plugin_policies`, and `[plugins].required` / `recommended` are rejected.
The old recommendation bridge methods have been replaced by
`clientPolicy.claimPlugins` and `clientPolicy.acceptPlugins`. Confirmation keys
are stored in `client-policy.seen`. No old-format aliases, migration reader or
compatibility bridge is provided.

The checked-in policies use these exact scopes:

| Builds | Plugin | Rules |
| --- | --- | --- |
| cn-279, cn-324, cn-404, cn-406, cn-408, cn-706, cn-717, cn-735, cn-774 | `bubblefighter.fantasy.login-fix-cn` | `require`, `latest` |
| kr-1256 | `bubblefighter.fantasy.login-fix-kr` | `require`, `latest` |
| kr-1256 | `bubblefighter.fantasy.shop-unlock` | `require` |
| kr-812, id-400, id-1809 | `bubblefighter.fantasy.font-support-cn` | `recommend` |

There are 24 explicit rule rows across thirteen builds. Required components do not
also declare recommendations. Compatibility, localized warning text and file
restoration behavior remain package metadata. Inclusion in a policy never enables
a build disabled by its client or server configuration.
