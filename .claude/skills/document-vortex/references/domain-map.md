# Vortex documentation domain map

Use this as a starting map for update-mode impact detection. Refine using project references and actual code calls.

| Path pattern | Primary documentation domain | Usually impacts |
|---|---|---|
| `Vortex.Main/**` | runtime/startup | hosting, Orleans, networking, deployment |
| `Vortex.Runtime/**` | runtime | sessions, presence, cross-domain runtime |
| `Vortex.Networking/**` | network | packet pipeline, sessions |
| `Vortex.Pipeline/**` | packet pipeline | protocol, handlers |
| `Vortex.Protocol/**` | protocol core | pipeline, revisions |
| `Vortex.Messages/**` | message/composer model | protocol, handlers |
| `Vortex.PacketHandlers/**` | packet orchestration | target gameplay domains, protocol |
| `Vortex.Revisions/**` | embedded revision | protocol, affected packet flows |
| `Vortex.Primitives/**` | shared contracts | every dependent domain; inspect dependency graph |
| `Vortex.Rooms/**` | rooms | furniture, wireds, navigator, persistence |
| `Vortex.Furniture/**` | furniture | rooms, inventory, catalog |
| paths containing `Wired` | wireds | rooms, protocol, persistence |
| `Vortex.Players/**` | players/presence | auth, social, economy, rooms |
| `Vortex.Authentication/**` | authentication | session startup, players |
| `Vortex.Database/**` | database | all domains touched by entities/migrations |
| `Vortex.WebApi/**` | public/web API | auth, data/live operations |
| `Vortex.Plugins/**` | plugins | runtime/extensibility/revisions |
| `Vortex.Observability/**` | observability | operations |
| `Vortex.Logging/**` | logging | operations |
| `Vortex.LoadGen/**` | load testing | performance/testing |
| `Vortex.Benchmark/**` | benchmarks | performance/testing |
| `Directory.Build.*` | global architecture/build | overview, testing |
| `Directory.Packages.props` | dependencies | overview, affected packages |
| `.claude/**`, `AGENTS.md`, `CONTEXT.md` | AI/architecture contract | contributor guidance, validation |
