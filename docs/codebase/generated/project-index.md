# Project index

> **Generated reference index.** This file inventories code symbols from a static scan of the
> repository at commit `e57f0be79a96` and should not be used as the sole source for runtime semantics.
> Regenerate with `/document-vortex update`. Explanatory pages live one directory up.


49 projects in `Vortex.Cloud.sln`. `Kind` is derived from `OutputType` and the SDK attribute in the `.csproj`: a project with no `OutputType` is a **library**, however service-like its name sounds. Responsibilities are described in [`../00-overview/solution-map.md`](../00-overview/solution-map.md).

| Project | Kind | Direct project references | Key packages |
|---|---|---|---|
| `Vortex.Authentication` | Library | `Vortex.Crypto`, `Vortex.Database`, `Vortex.Players`, `Vortex.Primitives` | `Microsoft.Extensions.Hosting`, `BCrypt.Net-Next` |
| `Vortex.Authentication.Tests` | Test | `Vortex.Authentication`, `Vortex.Database`, `Vortex.Primitives` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Microsoft.EntityFrameworkCore.InMemory` |
| `Vortex.Benchmark` | Perf/tooling | `Vortex.Database`, `Vortex.Observability`, `Vortex.Primitives` | `Microsoft.Extensions.Hosting` |
| `Vortex.Catalog` | Library | `Vortex.Database`, `Vortex.Furniture`, `Vortex.Logging`, `Vortex.Observability`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Protocol` | `Microsoft.Extensions.Hosting` |
| `Vortex.Collectibles` | Library | `Vortex.Database`, `Vortex.Logging`, `Vortex.Primitives` | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Sdk` |
| `Vortex.Crypto` | Library | `Vortex.Primitives` | `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Hosting`, `BouncyCastle.Cryptography` |
| `Vortex.Crypto.Tests` | Test | `Vortex.Crypto` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `BouncyCastle.Cryptography` |
| `Vortex.Dashboard.API` | Library | `Vortex.Database`, `Vortex.Observability`, `Vortex.Primitives` | `Microsoft.EntityFrameworkCore`, `Microsoft.Orleans.Sdk`, `Swashbuckle.AspNetCore` |
| `Vortex.Dashboard.Tests` | Test | `Vortex.Dashboard.API`, `Vortex.Primitives` | `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.TestHost`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` |
| `Vortex.Database` | Library | `Vortex.Primitives` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Hosting`, `Pomelo.EntityFrameworkCore.MySql` |
| `Vortex.Database.Tests` | Test | `Vortex.Catalog`, `Vortex.Database`, `Vortex.Furniture`, `Vortex.Inventory`, `Vortex.Marketplace`, `Vortex.Observability`, `Vortex.Tests.Support` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.EntityFrameworkCore.Sqlite` |
| `Vortex.Events` | Library | `Vortex.Pipeline`, `Vortex.Runtime` | &mdash; |
| `Vortex.Furniture` | Library | `Vortex.Database`, `Vortex.Observability`, `Vortex.Players`, `Vortex.Primitives` | `Microsoft.Extensions.Hosting` |
| `Vortex.Hosting.Tests` | Test | `Vortex.Authentication`, `Vortex.Crypto`, `Vortex.Database`, `Vortex.Main`, `Vortex.Observability`, `Vortex.Plugins`, `Vortex.Primitives`, `Vortex.WebApi` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` |
| `Vortex.Inventory` | Library | `Vortex.Database`, `Vortex.Furniture`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Protocol` | `Microsoft.Extensions.Hosting` |
| `Vortex.LoadGen` | Perf/tooling | &mdash; | &mdash; |
| `Vortex.Logging` | Library | `Vortex.Primitives` | `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Hosting` |
| `Vortex.Main` | Executable | `Vortex.Authentication`, `Vortex.Benchmark`, `Vortex.Catalog`, `Vortex.Collectibles`, `Vortex.Crypto`, `Vortex.Dashboard.API`, `Vortex.Database`, `Vortex.Events`, `Vortex.Furniture`, `Vortex.Inventory`, `Vortex.LoadGen`, `Vortex.Logging`, `Vortex.Marketplace`, `Vortex.Messages`, `Vortex.Navigator`, `Vortex.Networking`, `Vortex.Observability`, `Vortex.PacketHandlers`, `Vortex.Pipeline`, `Vortex.Players`, `Vortex.Plugins`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Protocol`, `Vortex.Revisions`, `Vortex.Rooms`, `Vortex.Runtime`, `Vortex.Social`, `Vortex.WebApi` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Tools`, `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Persistence.Memory`, `Microsoft.Orleans.Server`, &hellip; |
| `Vortex.Marketplace` | Library | `Vortex.Database`, `Vortex.Inventory`, `Vortex.Players`, `Vortex.Primitives` | `Microsoft.Extensions.Hosting` |
| `Vortex.Messages` | Library | `Vortex.Logging`, `Vortex.Pipeline`, `Vortex.Primitives`, `Vortex.Runtime` | &mdash; |
| `Vortex.Navigator` | Library | `Vortex.Database`, `Vortex.Players`, `Vortex.Primitives` | `Microsoft.Extensions.Hosting` |
| `Vortex.Navigator.Tests` | Test | `Vortex.Database`, `Vortex.Navigator`, `Vortex.Primitives`, `Vortex.Tests.Support` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Microsoft.EntityFrameworkCore.InMemory` |
| `Vortex.Networking` | Library | `Vortex.Crypto`, `Vortex.Messages`, `Vortex.Primitives`, `Vortex.Protocol` | `SuperSocket.ProtoBase`, `SuperSocket.Server`, `SuperSocket.WebSocket.Server` |
| `Vortex.Observability` | Library | `Vortex.Database`, `Vortex.Events`, `Vortex.Primitives` | `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Sdk` |
| `Vortex.PacketHandlers` | Library | `Vortex.Catalog`, `Vortex.Crypto`, `Vortex.Logging`, `Vortex.Messages`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Protocol`, `Vortex.Social` | `Microsoft.Extensions.Hosting` |
| `Vortex.Pipeline` | Library | `Vortex.Primitives`, `Vortex.Runtime` | `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyModel`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Vortex.Pipeline.Tests` | Test | `Vortex.Pipeline`, `Vortex.Runtime` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Microsoft.Extensions.DependencyInjection` |
| `Vortex.Players` | Library | `Vortex.Crypto`, `Vortex.Database`, `Vortex.Events`, `Vortex.Logging`, `Vortex.Messages`, `Vortex.Primitives`, `Vortex.Protocol` | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Sdk`, `Microsoft.Orleans.Streaming`, `Microsoft.Orleans.Persistence.Memory` |
| `Vortex.Players.Tests` | Test | `Vortex.Collectibles`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Social`, `Vortex.Tests.Support` | `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` |
| `Vortex.Plugins` | Library | `Vortex.Database`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Runtime` | `Microsoft.EntityFrameworkCore` |
| `Vortex.Plugins.TestPlugin` | Library | `Vortex.Primitives` | &mdash; |
| `Vortex.Plugins.Tests` | Test | `Vortex.Plugins.TestPlugin`, `Vortex.Plugins`, `Vortex.Primitives`, `Vortex.Runtime` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` |
| `Vortex.Primitives` | Library | &mdash; | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Sdk` |
| `Vortex.Progression` | Library | `Vortex.Database`, `Vortex.Events`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Protocol` | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Sdk` |
| `Vortex.Protocol` | Library | `Vortex.Primitives` | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Sdk` |
| `Vortex.Revisions` | Library | `Vortex.Logging`, `Vortex.Messages`, `Vortex.Primitives`, `Vortex.Protocol` | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Streaming`, `Microsoft.Orleans.Persistence.Memory` |
| `Vortex.Revisions.Tests` | Test | `Vortex.Primitives`, `Vortex.Protocol`, `Vortex.Revisions` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` |
| `Vortex.Rooms` | Library | `Vortex.Database`, `Vortex.Furniture`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Protocol`, `Vortex.Runtime` | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Streaming`, `Microsoft.Orleans.Persistence.Memory` |
| `Vortex.Rooms.Tests` | Test | `Vortex.Benchmark`, `Vortex.Collectibles`, `Vortex.Database`, `Vortex.Events`, `Vortex.Furniture`, `Vortex.LoadGen`, `Vortex.Observability`, `Vortex.Players`, `Vortex.Primitives`, `Vortex.Progression`, `Vortex.Protocol`, `Vortex.Rooms`, `Vortex.Social`, `Vortex.Tests.Support` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.Orleans.TestingHost` |
| `Vortex.Runtime` | Library | &mdash; | `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Hosting` |
| `Vortex.Social` | Library | `Vortex.Database`, `Vortex.Events`, `Vortex.Logging`, `Vortex.Primitives`, `Vortex.Protocol`, `Vortex.Runtime` | `Microsoft.Extensions.Hosting`, `Microsoft.Orleans.Sdk` |
| `Vortex.Supervisor` | Executable (web SDK) | `Vortex.Logging`, `Vortex.Primitives` | &mdash; |
| `Vortex.Supervisor.Tests` | Test | `Vortex.Supervisor` | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` |
| `Vortex.Tests.Support` | Library | &mdash; | `Microsoft.Orleans.Core.Abstractions` |
| `Vortex.WebApi` | Library | `Vortex.Database`, `Vortex.Primitives` | `BCrypt.Net-Next`, `Microsoft.EntityFrameworkCore`, `OpenTelemetry`, `OpenTelemetry.Exporter.Prometheus.AspNetCore`, `OpenTelemetry.Extensions.Hosting`, `Swashbuckle.AspNetCore` |
| `Vortex.WebApi.Tests` | Test | `Vortex.Database`, `Vortex.Primitives`, `Vortex.WebApi` | `Microsoft.AspNetCore.TestHost`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` |
