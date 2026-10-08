---
paths:
  - "Vortex.*.Tests/**"
  - "Vortex.Tests.Support/**"
---

# Tests

- xUnit + FluentAssertions. Fakes are `FakeProxy.Create<IInterface>(call => …)` (`Vortex.Tests.Support`):
  return `null` for calls the test does not care about, and the proxy answers a completed task or a
  default value.
- A grain is built with `GrainActivationContext.CreateWithIntegerKey<TGrain>(key, …ctor args)`. The
  arguments are `params object[]`: a constructor change compiles everywhere and fails at activation.
  After changing a grain constructor, grep the tests for `CreateWithIntegerKey<ThatGrain>`.
- A room under test comes from `RoomHarness.CreateAsync()` (`Vortex.Rooms.Tests/Support`); a packet
  handler from its constructor with fakes (`Vortex.PacketHandlers.Tests/SsoTicketMessageHandlerTests.cs`).
- EF InMemory does not run `ExecuteUpdate`/`ExecuteDelete` — it does nothing and says nothing. A test
  about those uses SQLite (`Vortex.Database.Tests/Support/SqliteTestDb.cs`,
  `RoomPersistenceLossWindowTests`).
- Write the failing test first when fixing a bug, and name tests as the behaviour they pin
  (`WithoutAStartZone_ThePlayerStillSignsIn`).
- Run one project while iterating (`dotnet test Vortex.Rooms.Tests --filter …`); the full gate
  before calling it done.
