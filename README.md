# jj2bot-client

A bot that can connect to jj2 servers, chat and imitate a real client.
Works both on TCP and UDP protocol. 

The TCP protocol is used for connecting, chat packets etc.
UDP protocol is used for gameplay packets.

## Build and run

This is a Windows Forms client targeting .NET Framework 4.0 Client Profile, x86.
Open `Jj2.csproj` in Visual Studio with the matching .NET Framework targeting
tools installed, or build from a Visual Studio Developer PowerShell:

```powershell
MSBuild.exe .\Jj2.csproj /p:Configuration=Release /p:Platform=x86
& '.\bin\Release\Jj2 Client.exe'
```

The repository includes the server browser, TCP/UDP client, chat, character
selection, map parser and renderer, asset downloads, spectator controls,
local movement/physics, packet-capture proxy source and regression tests.
`Jj2Proxy.cs` is a separate diagnostic utility, not part of the GUI build.
Game levels, tilesets, downloaded caches, logs and compiled binaries are not
included; use your existing JJ2 installation and the server download support.

## Host a server

Click **Host a server** next to the character picker in the server browser.
Select a folder containing your JJ2 levels and tilesets, a `.j2l` level, the
server name, TCP/UDP port and player limit, then click **Start server**.
This starts **our own TCP/UDP server inside the client application**.
It does not launch or depend on `Jazz2.exe`; only the level and tileset files
are needed. The asset folder is remembered locally.

The server window displays connected player counts and a live log.
**Join server** connects our bot client to `127.0.0.1` on the configured port.
Native JJ2+ clients can connect directly, for example:

```powershell
.\Jazz2.exe -connect 127.0.0.1:10052 -windowed
```

For another computer, replace `127.0.0.1` with the server computer's LAN/VPN IP.
**Stop server**, closing the server window, or closing the application stops
hosting, disconnects clients and releases both ports. No separate game process
has to be closed.

**This is an experimental networking sandbox using the Battle protocol, not
a complete JJ2 server engine.** It implements native discovery, JJ2+ negotiation,
single-player joins (up to 31 clients), roster updates, chat, spectator toggles,
validated map/tileset downloads, keepalives, movement relay and visual blaster
event relay. TCP control and UDP gameplay are handled by our server sockets.
Downloads expose only immutable snapshots of the selected level and its tileset;
arbitrary file requests are rejected.

There is **no authoritative combat/damage, pickup synchronization, CTF flags or
scoring, AngelScript, level cycling, password authentication, splitscreen or
public-list registration**. Unsupported packets/commands are reported. Do not
treat visible projectiles as implemented combat. Use trusted LAN/VPN peers;
this prototype is not intended as a production internet server. The application
does not change firewall/router settings. Access across NAT would require
forwarding **both TCP and UDP** on the selected port (default `10052`).

Run `powershell -File .\tests\RunHostingTests.ps1` for synthetic protocol,
real-socket, download, chat, movement, lifecycle and UI checks. Optional
interoperability checks use your own game assets and native JJ2+ as a *client*:

```powershell
.\tests\RunHostingTests.ps1 -AssetDirectory 'C:\games\Jazz2+' -NativeExecutable 'C:\games\Jazz2+\Jazz2.exe'
```

These checks host our standalone server, download its assets into a temporary
bot cache, connect two actual bot clients and optionally connect a native JJ2+
client. The native executable is not used to host the server.

## Map downloads

The client checks the level and tileset against the CRC32 values in server details
(`0x10`). Missing, outdated or corrupt assets are downloaded directly from the
connected server before gameplay readiness is sent. The tileset is required even
for the collision-only map: tile collision masks are stored in `.j2t`, not `.j2l`.

Validated downloads are cached under `GameDirectory\cache\bot\<CRC32>\<filename>`.
Existing game files are never overwritten. Reconnecting reuses matching cached
files. The map view renders the playable sprite layer in color using the tileset
palette, graphics, transparency and tile flips. Animated tiles use their first
frame; parallax backgrounds, foreground layers and scripted graphics are not
rendered. Collision masks remain separate and continue to drive physics.
Transfers have a 15-second inactivity timeout and a 32 MiB per-file limit.
Invalid filenames, sizes, piece order or checksums stop the connection with an
explicit error instead of loading an incorrect map.

The following download packets were captured and verified against JJ2+:

| Direction | Opcode | Payload (after normal/extended TCP framing) |
|---|---|---|
| Client to server | `0x15` | Filename length (1 byte), ASCII filename (no trailing NUL) |
| Server to client | `0x14`, piece 0 | Piece number (uint32 LE, zero), file size (uint32 LE), filename length (1 byte), ASCII filename |
| Server to client | `0x14`, pieces 1+ | Consecutive piece number (uint32 LE), raw file data |

After `0x13`, the client requests the level if necessary, reads its tileset name,
requests that tileset if necessary, validates both files and loads the collision
map before completing the existing ready handshake. No per-piece ACK was needed
in the captured transfer. Completion is determined by the announced file size.
For `.j2l`/`.j2t`, the embedded CRC32 at offset 226 covers the bytes from offset
262 to EOF; it must also match the corresponding server checksum.

## Spectator control

The map toolbar has a Spectator button in place of the Follow/Trails checkboxes.
Click to request ON/OFF on JJ2+ servers. The indicator uses the server's initial
spectator bitmask and subsequent state updates (`0x41`), with spectator console
responses handling confirmations and rejections. Unconfirmed requests are not shown
as successful state changes. If the server disables spectating, it displays
`OFF (server blocked)`. A later click can retry after the host enables it.
Movement and firing controls are suppressed while spectating; chat and map
updates remain available.
Spectating players appear gray in the player list, without an extra name label.
Active players retain their team colors. The list follows the server's initial
spectator bitmask and live updates, including other players' toggles.
Spectator state is keyed by socket ID, not player ID: splitscreen players share
one socket, so their presence must not shift another client's spectator status.

## Gameplay controls

Enable keyboard control and focus the connection/map window, not the chat input.
Arrows move, Space jumps and Ctrl fires the basic blaster (about one shot per
0.51 seconds without fast fire). Up+Ctrl aims vertically; Down+Ctrl crouch-fires.
Shooting sends a real projectile event, not just a movement button flag.
The current implementation does not collect/switch weapons or apply fast-fire
pickups.

Spaz gets one extra jump per airborne trip: release and press Space again.
For his sidekick, crouch with Down for at least 0.2 seconds, then press Space.
Jazz and Lori use helicopter ears by holding Space while descending; releasing
lets the current copter interval finish, and another press can reactivate it.
All three can buttstomp with Down in the air, including the windup and landing
recovery. Down while hanging still drops from the vine/hook instead of stomping.
Springs, landing and teleportation reset the relevant special-move state.
Server death events (UDP `0x07`, event `0x0F`) and lethal combat hits (`0x0C`,
zero health in the packed health/player byte) respawn our rabbit at a level
start using the same selection as the initial spawn.
Other players' deaths and duplicate death datagrams do not teleport our rabbit.
UDP `0x07` may batch multiple length-prefixed events in one datagram. Each event
is parsed independently, so combat hits followed by flag/projectile updates are
not rejected as malformed death packets.
This remains a local collision/physics approximation, not the complete JJ2 engine.

The movement button byte uses `0x08` for jump and `0x02` for fire, verified with
real-client input. Blaster shots use UDP `0x08`, a little-endian payload size
of 16 and event `0x22`; the server relays these as UDP `0x07`. Projectile IDs
combine the player ID and a ten-bit counter. Shot position, signed 8.8 velocity,
aiming flags and blaster fields are encoded from captured horizontal/up/crouching
shots. Jump, double-jump, copter, sidekick and stomp animation IDs were also
checked against real Jazz/Spaz/Lori clients.

Run the dependency-free physics and captured-packet regression checks with
`powershell -File .\tests\RunGameplayTests.ps1`.
`powershell -File .\tests\RunConnectionRegressionTests.ps1` covers batched events
and mouse Disconnect clicks held across keyboard-control timer ticks.
`powershell -File .\tests\RunSpectatorListTests.ps1` covers spectator state and
player-list rendering.
