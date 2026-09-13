# CustomItemLoader Changelog

## 1.01.001 — 2026-09-14

Rust / uMod API compatibility update.

### Changed

- Plugin version updated from `1.00.000` to `1.01.001`.
- `CommandForceJump` migrated from the legacy `ClientRPCPlayer(...)` call to:
  `ClientRPC(RpcTarget.Player("ForcePositionTo", player), ...)`.
- `CmdCilGiveConsole` now explicitly converts `ConsoleSystem.Arg.Args` values with `.ToString()` before string use / integer parsing.

### Compatibility workaround

- The current Rust API no longer accepts the legacy string mode `"Movement"` for the `SetPlayerSpeed` RPC.
- The `runspeed` passive is therefore temporarily disabled instead of sending an obsolete RPC signature.
- The corresponding legacy speed-reset RPC in `RemovePassiveEffects` is also disabled.
- Other passive/effect handling remains in place.

### Reason

The previous implementation could fail after the Rust update because it called RPC/API signatures that no longer match the current server-side types.

This release prioritizes safe operation: incompatible speed RPC calls are not sent until the current `SetPlayerSpeed` mode mapping is confirmed.

### Verification note

- Release source: server-tested `CustomItemLoader.cs` supplied after the Rust update.
- `runspeed` being unavailable is intentional in this release and is a compatibility safeguard, not a permanent feature removal.
