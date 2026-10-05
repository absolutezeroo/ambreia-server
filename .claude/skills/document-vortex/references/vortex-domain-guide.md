# Vortex domain guide

This guide is a research checklist, not a statement of implementation truth.

## Core architecture
- `Vortex.Main`
- `Vortex.Runtime`
- `Vortex.Primitives`
- hosting/bootstrap/configuration/build files

## Networking/protocol
- `Vortex.Networking`
- `Vortex.Pipeline`
- `Vortex.Protocol`
- `Vortex.Messages`
- `Vortex.PacketHandlers`
- `Vortex.Revisions`

## Game domains
- `Vortex.Players`
- `Vortex.Rooms`
- `Vortex.Furniture`
- `Vortex.Navigator`
- `Vortex.Social`
- `Vortex.Catalog`
- `Vortex.Inventory`
- `Vortex.Marketplace`
- `Vortex.Collectibles`
- `Vortex.Progression`

Search for additional domain projects before assuming this list is complete.

## Persistence
- `Vortex.Database`
- migrations/configurations/entities
- Orleans persistent state configuration and stores

## Administration
- `Vortex.Dashboard.API`
- `Vortex.Dashboard.Web`
- `Vortex.WebApi`

## Extensibility/operations
- `Vortex.Plugins`
- `Vortex.Observability`
- `Vortex.Logging`
- `Vortex.Supervisor`
- `Vortex.LoadGen`
- `Vortex.Benchmark`

## Important repository-specific distinctions

- `Vortex.Primitives` being widely referenced does not mean it should contain domain logic.
- Packet handlers are orchestration boundaries, not ownership boundaries.
- Revision-specific parsing/serialization is distinct from domain messages and behavior.
- `Revision20260701` is embedded core; additional revisions belong in the external plugin repo according to the repository contract.
- Dashboard mutations of grain-owned state must use live ownership paths, not merely DB writes.
