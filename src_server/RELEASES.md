# Changelog

## [0.12.0](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/compare/server-v0.11.0...server-v0.12.0) (2026-10-12)


### Features

* a round has an identity across the wire, and no job starts without budget (round counter E4) ([#766](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/766)) ([722fc59](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/722fc5975b4b203250e10c1cd01ebc2c19e88ccd))
* **tests:** local test runs take at most 8 workers, at lowered CPU priority ([#757](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/757)) ([328703f](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/328703faf67d84ebb55944922215925157dc85aa))
* the ledger tells the truth — whole usage, end causes, one line per paid launch, what each ran with (round counter E3) ([#761](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/761)) ([8d086c5](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/8d086c510518de52e7786f0d0b8ebd7f9a323a64))
* the Team server counts rounds per vendor and window, and a stated reset is read, shown and never waited out (round counter E5) ([#773](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/773)) ([bb95c1c](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/bb95c1c4074a9e3d09d09412e8d4674f09bb9438))


### Bug Fixes

* **server:** a stated reset duration parks the slot until then (story 5.3a) ([#754](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/754)) ([7b9d092](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/7b9d092845330253314f6e37e0e34600d4588f7d))
* **server:** the image checks each vendor installer before it runs it, and retries a bad answer ([#759](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/759)) ([5b91a78](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/5b91a787a1cd6388b72188485d704ba2790d9d12))
* **server:** the image installs agy from its release manifest, checked, and a PR builds the image ([#775](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/775)) ([2f31542](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/2f31542144b22049a3292fcbb7d3e8d2a8adfcf0))

## [0.11.0](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/compare/server-v0.10.0...server-v0.11.0) (2026-10-10)


### Features

* the Team server tab — who on the team spent what, for an admin ([#737](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/737)) ([3ed2f15](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/3ed2f1538bf6285ece2869e8be5711b5e685f899))

## [0.10.0](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/compare/server-v0.9.0...server-v0.10.0) (2026-10-09)


### Features

* model catalog epic 2, part 1 — runtime-true resolution, --features, Bugz by runtime ([#686](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/686)) ([ef2a391](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/ef2a39199c558cdcf67e8bd2031d8436c04b8eec))


### Bug Fixes

* a port somebody else holds ends both HTTP hosts with exit 75 and one line, never a crash ([8cc4f49](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/8cc4f49216f8e33da5b1fc4e8f5744ce36d524a5))
* an agy launch cannot write inside its roots — a reader agent and an allowlist hook ([#716](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/716)) ([ec1c912](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/ec1c9122b00b85782e803278da108b008622c652))
* **mcp,server:** messages name ConnectOtherAIs &gt; Team servers / Consultant / MCP server (S4, part 1) ([7310aff](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/7310affe3f45749e1def7d03a20c8d18611cf7b3))
* the bind-failure line is one ASCII line on stderr, with no stack and no dropped log call ([e21d4bc](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/e21d4bc57d0515c82e706e9d769e81f265631b00))

## [0.9.0](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/compare/server-v0.8.0...server-v0.9.0) (2026-09-26)


### Features

* the feature review, epic 2 — review_feature, the hybrid pack, the gate's history ([726943b](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/726943b57d6be506c9c69cf4ac38b69e58b75992))

## [0.8.0](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/compare/server-v0.7.0...server-v0.8.0) (2026-09-25)


### Features

* the feature review, epic 1 — honest stages, an api runtime, a seven-language outline ([d8c0bca](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/d8c0bcaada76949a24d4ce94b1c6e5e32467872f))

## [0.7.0](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/compare/server-v0.6.2...server-v0.7.0) (2026-09-25)


### Features

* **ci:** release-please for the four lines, option A, on a button ([81337d1](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/81337d144beab55a948e126cba9233a427afbc9e))


### Bug Fixes

* **server:** a signal ends the server cleanly; reviewers start no MCP servers ([#514](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/514)) ([89b4986](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/89b49860d4c810efb1c81a7a46a09c8034749d51))
* **server:** continue an auto-denied agy reviewer in its own conversation ([bb66973](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/bb66973d7c677181924d74ae8d285625eb4a97c0)), closes [#515](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/515)
* **server:** the follow-up reads "nothing" once, and says when it did not run ([#515](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/515), code round) ([573b3ac](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/573b3ac451a693b5a4016439ebee22d34b2aedcf))
* **server:** the stop really takes the ordinary road; the codex reader never invents a name ([#514](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/514), code round) ([b35c580](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/commit/b35c580b12b0d96cf59f77f422e8882f0ce73bfe))
