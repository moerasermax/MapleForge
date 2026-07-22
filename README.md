# MapleForge

Modern .NET 10 reimplementation of a MapleStory TMS v113 server — Clean/Hexagonal Architecture with full protocol verification via packet fixtures, headless client testing, and real capture replay.

## Highlights

- **Clean/Hexagonal Architecture**: Strict layered separation — `Core`, `Application`, `Net`, `Persistence`, `Content`, `Adapters.V113` — with Roslyn analyzers enforcing that Core/Application never reference version-specific assemblies
- **750+ Automated Tests**: Core (111), Adapters.V113 (426), HeadlessClient (29), PacketDecoder (22), Persistence (11, EphemeralMongo), Net (2), Application (140+), Content (9+)
- **Triple Verification Strategy**: Each protocol handler is classified as `Java-source candidate`, `capture verified`, or `live verified` — documentation distinguishes inference from proven behavior
- **Headless Client**: Full client simulator that authenticates, selects channel/character, and exercises game flows without a graphical client — enables automated integration testing
- **Packet Decoder Tooling**: Offline packet decoder with golden fixture tests against real network captures — provides a closed evidence chain from wire bytes to named operations
- **Bounded Outbound Channel**: `Channel<T>` with capacity limit prevents slow clients from consuming unbounded server memory; single-writer cipher mutation eliminates concurrency bugs in encryption state
- **Jint NPC Scripting**: JavaScript NPC/quest/reactor scripts via Jint engine, replacing the legacy Nashorn dependency

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# / .NET 10 |
| Architecture | Clean Architecture + DDD |
| Hosting | Microsoft Generic Host / DI |
| Networking | System.Net.Sockets, bounded Channel\<T\> |
| Crypto | Maple AES-OFB (custom implementation) |
| Scripting | Jint (JavaScript engine) |
| Persistence | LiteDB / MongoDB (EphemeralMongo for tests) |
| Testing | xUnit, golden fixture verification |

## Architecture

```
┌─────────────────────────────────────────────┐
│                   Host                       │
│         (Generic Host, DI wiring)            │
├──────────┬──────────┬───────────────────────┤
│  Net     │ Content  │   Adapters.V113        │
│ (Socket  │ (WZ data │  (v113 packet codec,   │
│  server) │  loader) │   handler routing)     │
├──────────┴──────────┴───────────────────────┤
│              Application                     │
│     (Use cases, session management)          │
├─────────────────────────────────────────────┤
│                 Core                         │
│  (Domain entities, value objects, ports)     │
├─────────────────────────────────────────────┤
│             Persistence                      │
│    (LiteDB / MongoDB repositories)           │
└─────────────────────────────────────────────┘

  Roslyn analyzers enforce: Core ← Application (no upward leaks)
```

## Design Decisions

| Legacy Pain Point | MapleForge Approach |
|---|---|
| O(n) opcode scan per packet | Dictionary + attribute-routed dispatch |
| 8,000+ line god classes | Domain decomposition across bounded contexts |
| Static singletons, hand-ordered boot | DI container + hosted services |
| Nashorn (removed in JDK 15) | Jint (.NET-native JS engine) |
| Zero tests | 750+ tests with fixture verification |
| All-in-one monolith | Clean Architecture with enforced layer boundaries |

## Context

This is a from-scratch rewrite of a legacy Java server (OdinMS lineage). The legacy codebase served as behavioral reference only — all code is original C#. The rewrite demonstrates applying modern software engineering practices (DDD, Clean Architecture, comprehensive testing) to a complex real-time networked system.

WZ game data files are Nexon client assets and are not included in this repository.
