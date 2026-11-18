# MonoCache

A toy in-memory cache server written in C#. Built as a learning exercise covering TCP socket programming, LRU eviction, and write-ahead logging. Not intended for production use.

Originally written in .NET 8, migrated to .NET 10.

---

## What it does

Key/value store over TCP. Clients connect on port 6969, send binary commands, and get binary responses back. Values are serialized to JSON by the client library before going over the wire, so any JSON-serializable type can be stored.

The cache has three behaviours working together to manage memory and durability:

**LRU eviction** - the cache holds at most 100 items. When that cap is hit, the least recently used item is dropped to make room. Every GET and PUT on an existing key counts as a use and moves that item to the front of the eviction order.

**Sliding TTL** - each item has a 5-minute lifetime that resets on every access. An item that is read frequently will stay alive indefinitely. An item that is written once and never touched again expires after 5 minutes. Expired items are removed lazily on the next GET for that key, or during a compaction pass.

**Write-ahead log (WAL)** - every PUT and CLEAR is appended to `WAL/monocacheWAL.txt` as a plain-text pipe-delimited line before the in-memory state changes. This means the cache can survive a process restart.

## Cache rebuild on startup

When `MonoCacheServer` starts, `MonoCacheCore` checks for an existing WAL file and replays it from top to bottom before accepting any connections. The replay reconstructs the in-memory state as it was at shutdown:

- `PUT` entries are re-inserted with their original expiry timestamps. Any entry whose expiry has already passed is silently skipped - there is no point restoring stale data.
- `CLEAR` entries wipe the in-memory state at the point in the log they appear, just as they did originally. This means a CLEAR that happened mid-session correctly discards everything before it.
- After replay, a compaction pass runs to drop any remaining expired entries and enforce the 100-item cap.
- The WAL file is then deleted and a fresh one is started for the new session.

The result is that restarting the server restores all keys that were still alive at the time of shutdown, while discarding anything that would have expired in the interim anyway.

## Commands

| Command | Byte | Description |
|---------|------|-------------|
| PUT     | 1    | Store or overwrite a value |
| GET     | 2    | Retrieve a value; returns null if missing or expired |
| CLEAR   | 3    | Remove all keys |

The wire protocol is binary: a command byte followed by length-prefixed (Int32 little-endian) key and value payloads. GET responses are either a single `0x00` byte (miss) or `0x01` followed by the length-prefixed value bytes.

## Projects

**MonoCache** - the server library. `MonoCacheCore` is the in-memory engine (LRU + TTL + WAL). `MonoCacheServer` wraps it in a TCP listener on port 6969 and dispatches one goroutine-style `Task` per connected client.

**MonoCache.Client** - client library. `MonoCacheClient` holds a persistent TCP connection and exposes `Put<T>`, `Get<T>`, and `Clear`. Values are serialized to/from JSON via `System.Text.Json`. Thread-safe - a single client instance can be shared across threads.

**MonoCacheServerTest** - runs the server standalone and waits for Enter. Use this when you want a live server to test against manually or to exercise the WAL rebuild (start the server, write some keys, kill it, restart it and verify the keys come back).

**MonoCacheTest** - integration test suite. If no server is already running on port 6969 it starts its own; otherwise it connects to the existing one. Runs assertions covering PUT/GET, missing keys, updates, CLEAR, multiple data types, and two-client isolation. Exits with code 1 on any failure.

## Running

```
dotnet run --project MonoCacheServerTest   # run the server
dotnet run --project MonoCacheTest         # run the integration tests
```

Both can run simultaneously. `MonoCacheTest` detects the live server and skips starting its own.

## Known limitations

- Port 6969 and max capacity (100 items) are hardcoded
- Synchronous blocking I/O throughout, no async
- No DEL command - a single key can only be removed by clearing everything
- WAL grows unbounded within a session; it is only compacted (and rewritten) on restart
- No auth, no TLS
