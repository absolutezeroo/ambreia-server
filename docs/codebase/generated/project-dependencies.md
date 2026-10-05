# Project dependency index

> **Generated reference index.** This file inventories code symbols from a static scan of the
> repository at commit `e57f0be79a96` and should not be used as the sole source for runtime semantics.
> Regenerate with `/document-vortex update`. Explanatory pages live one directory up.


## Most-referenced projects

In-degree = how many projects in the solution reference this one directly. A high in-degree marks a contracts hub, not necessarily a layer that owns behaviour.

| Project | Referenced by | Kind |
|---|---|---|
| `Vortex.Primitives` | 37 | Library |
| `Vortex.Database` | 23 | Library |
| `Vortex.Logging` | 12 | Library |
| `Vortex.Protocol` | 12 | Library |
| `Vortex.Players` | 10 | Library |
| `Vortex.Runtime` | 9 | Library |
| `Vortex.Observability` | 8 | Library |
| `Vortex.Crypto` | 7 | Library |
| `Vortex.Furniture` | 6 | Library |
| `Vortex.Events` | 6 | Library |
| `Vortex.Messages` | 5 | Library |
| `Vortex.Tests.Support` | 4 | Library |
| `Vortex.Pipeline` | 4 | Library |
| `Vortex.Progression` | 4 | Library |
| `Vortex.Social` | 4 | Library |
| `Vortex.Authentication` | 3 | Library |
| `Vortex.Catalog` | 3 | Library |
| `Vortex.Inventory` | 3 | Library |
| `Vortex.Plugins` | 3 | Library |
| `Vortex.WebApi` | 3 | Library |
| `Vortex.Collectibles` | 3 | Library |
| `Vortex.Dashboard.API` | 2 | Library |
| `Vortex.Marketplace` | 2 | Library |
| `Vortex.Benchmark` | 2 | Perf/tooling |
| `Vortex.LoadGen` | 2 | Perf/tooling |
| `Vortex.Navigator` | 2 | Library |
| `Vortex.Revisions` | 2 | Library |
| `Vortex.Rooms` | 2 | Library |
| `Vortex.Main` | 1 | Executable |
| `Vortex.Networking` | 1 | Library |
| `Vortex.PacketHandlers` | 1 | Library |
| `Vortex.Plugins.TestPlugin` | 1 | Library |
| `Vortex.Supervisor` | 1 | Executable (web SDK) |

## Roots (referenced by no other project)

- `Vortex.Authentication.Tests` &mdash; Test
- `Vortex.Crypto.Tests` &mdash; Test
- `Vortex.Dashboard.Tests` &mdash; Test
- `Vortex.Database.Tests` &mdash; Test
- `Vortex.Hosting.Tests` &mdash; Test
- `Vortex.Navigator.Tests` &mdash; Test
- `Vortex.Pipeline.Tests` &mdash; Test
- `Vortex.Players.Tests` &mdash; Test
- `Vortex.Plugins.Tests` &mdash; Test
- `Vortex.Revisions.Tests` &mdash; Test
- `Vortex.Rooms.Tests` &mdash; Test
- `Vortex.Supervisor.Tests` &mdash; Test
- `Vortex.WebApi.Tests` &mdash; Test

## Adjacency (outgoing)

| Project | Depends on |
|---|---|
| `Vortex.Authentication` | `Vortex.Crypto`, `Vortex.Database`, `Vortex.Players`, `Vortex.Primitives` |
| `Vortex.Authentication.Tests` | `Vortex.Authentication`, `Vortex.Database`, `Vortex.Primitives` |
| `Vortex.Benchmark` | `Vortex.Database`, `Vortex.Observability`, `Vortex.Primitives` |
| `Vortex.Catalog` | `Vortex.Database`, `Vortex.Furniture`, `Vortex.Logging`, `Vortex.Observability`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Protocol` |
| `Vortex.Collectibles` | `Vortex.Database`, `Vortex.Logging`, `Vortex.Primitives` |
| `Vortex.Crypto` | `Vortex.Primitives` |
| `Vortex.Crypto.Tests` | `Vortex.Crypto` |
| `Vortex.Dashboard.API` | `Vortex.Database`, `Vortex.Observability`, `Vortex.Primitives` |
| `Vortex.Dashboard.Tests` | `Vortex.Dashboard.API`, `Vortex.Primitives` |
| `Vortex.Database` | `Vortex.Primitives` |
| `Vortex.Database.Tests` | `Vortex.Catalog`, `Vortex.Database`, `Vortex.Furniture`, `Vortex.Inventory`, `Vortex.Marketplace`, `Vortex.Observability`, `Vortex.Tests.Support` |
| `Vortex.Events` | `Vortex.Pipeline`, `Vortex.Runtime` |
| `Vortex.Furniture` | `Vortex.Database`, `Vortex.Observability`, `Vortex.Players`, `Vortex.Primitives` |
| `Vortex.Hosting.Tests` | `Vortex.Authentication`, `Vortex.Crypto`, `Vortex.Database`, `Vortex.Main`, `Vortex.Observability`, `Vortex.Plugins`, `Vortex.Primitives`, `Vortex.WebApi` |
| `Vortex.Inventory` | `Vortex.Database`, `Vortex.Furniture`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Protocol` |
| `Vortex.LoadGen` | &mdash; |
| `Vortex.Logging` | `Vortex.Primitives` |
| `Vortex.Main` | `Vortex.Authentication`, `Vortex.Benchmark`, `Vortex.Catalog`, `Vortex.Collectibles`, `Vortex.Crypto`, `Vortex.Dashboard.API`, `Vortex.Database`, `Vortex.Events`, `Vortex.Furniture`, `Vortex.Inventory`, `Vortex.LoadGen`, `Vortex.Logging`, `Vortex.Marketplace`, `Vortex.Messages`, `Vortex.Navigator`, `Vortex.Networking`, `Vortex.Observability`, `Vortex.PacketHandlers`, `Vortex.Pipeline`, `Vortex.Players`, `Vortex.Plugins`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Protocol`, `Vortex.Revisions`, `Vortex.Rooms`, `Vortex.Runtime`, `Vortex.Social`, `Vortex.WebApi` |
| `Vortex.Marketplace` | `Vortex.Database`, `Vortex.Inventory`, `Vortex.Players`, `Vortex.Primitives` |
| `Vortex.Messages` | `Vortex.Logging`, `Vortex.Pipeline`, `Vortex.Primitives`, `Vortex.Runtime` |
| `Vortex.Navigator` | `Vortex.Database`, `Vortex.Players`, `Vortex.Primitives` |
| `Vortex.Navigator.Tests` | `Vortex.Database`, `Vortex.Navigator`, `Vortex.Primitives`, `Vortex.Tests.Support` |
| `Vortex.Networking` | `Vortex.Crypto`, `Vortex.Messages`, `Vortex.Primitives`, `Vortex.Protocol` |
| `Vortex.Observability` | `Vortex.Database`, `Vortex.Events`, `Vortex.Primitives` |
| `Vortex.PacketHandlers` | `Vortex.Catalog`, `Vortex.Crypto`, `Vortex.Logging`, `Vortex.Messages`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Protocol`, `Vortex.Social` |
| `Vortex.Pipeline` | `Vortex.Primitives`, `Vortex.Runtime` |
| `Vortex.Pipeline.Tests` | `Vortex.Pipeline`, `Vortex.Runtime` |
| `Vortex.Players` | `Vortex.Crypto`, `Vortex.Database`, `Vortex.Events`, `Vortex.Logging`, `Vortex.Messages`, `Vortex.Primitives`, `Vortex.Protocol` |
| `Vortex.Players.Tests` | `Vortex.Collectibles`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Social`, `Vortex.Tests.Support` |
| `Vortex.Plugins` | `Vortex.Database`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Runtime` |
| `Vortex.Plugins.TestPlugin` | `Vortex.Primitives` |
| `Vortex.Plugins.Tests` | `Vortex.Plugins.TestPlugin`, `Vortex.Plugins`, `Vortex.Primitives`, `Vortex.Runtime` |
| `Vortex.Primitives` | &mdash; |
| `Vortex.Progression` | `Vortex.Database`, `Vortex.Events`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Protocol` |
| `Vortex.Protocol` | `Vortex.Primitives` |
| `Vortex.Revisions` | `Vortex.Logging`, `Vortex.Messages`, `Vortex.Primitives`, `Vortex.Protocol` |
| `Vortex.Revisions.Tests` | `Vortex.Primitives`, `Vortex.Protocol`, `Vortex.Revisions` |
| `Vortex.Rooms` | `Vortex.Database`, `Vortex.Furniture`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Protocol`, `Vortex.Runtime` |
| `Vortex.Rooms.Tests` | `Vortex.Benchmark`, `Vortex.Collectibles`, `Vortex.Database`, `Vortex.Events`, `Vortex.Furniture`, `Vortex.LoadGen`, `Vortex.Observability`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Protocol`, `Vortex.Rooms`, `Vortex.Social`, `Vortex.Tests.Support` |
| `Vortex.Runtime` | &mdash; |
| `Vortex.Social` | `Vortex.Database`, `Vortex.Events`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Protocol`, `Vortex.Runtime` |
| `Vortex.Supervisor` | `Vortex.Logging`, `Vortex.Primitives` |
| `Vortex.Supervisor.Tests` | `Vortex.Supervisor` |
| `Vortex.Tests.Support` | &mdash; |
| `Vortex.WebApi` | `Vortex.Database`, `Vortex.Primitives` |
| `Vortex.WebApi.Tests` | `Vortex.Database`, `Vortex.Primitives`, `Vortex.WebApi` |
