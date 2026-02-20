# SimHub AmbiMatter

Ambient room lighting that reacts to iRacing telemetry (time of day, weather,
tunnels) via Matter-compatible CCT smart bulbs.

## How It Works

```
SimHub (iRacing) → C# Plugin → UDP → Python Bridge → Matter → Smart Bulbs
```

1. The **C# SimHub plugin** reads iRacing telemetry, calculates a target color
   temperature (Kelvin) and brightness, and sends UDP packets to the bridge.
2. The **Python Matter bridge** receives those packets and sends Matter protocol
   commands to your bulbs, organized into named zones.

The system uses CCT (Kelvin + Brightness) only — no RGB. Bulbs smoothly
transition between states using Matter's built-in transition time, keeping
command frequency low enough to avoid crashing Wi-Fi Matter devices.

## Status

| Component | Status |
|---|---|
| Python Matter Bridge | Phase 1 — complete |
| C# SimHub Plugin | Phase 2 — complete |
| Weather layer (iRacing cloud/rain) | Phase 3 — planned |

## Quick Start

### 1 — Install the SimHub Plugin

Copy `SmartAmbientMatter.dll` (from the [latest release](../../releases/latest)) to:
```
C:\Program Files (x86)\SimHub\
```
Restart SimHub. **AmbiMatter** will appear in the left menu.

### 2 — Start the Python Bridge

```bash
cd bridge
pip install -r requirements.txt

# Edit config.yaml — set your Matter node IDs

# Test without hardware:
python ambient_bridge.py --dry-run &
python test_sender.py --fast

# Run for real:
python ambient_bridge.py
```

See [bridge/README.md](bridge/README.md) for full setup details.

## Hardware

- **Bulbs:** Linkind Matter A19 CCT (2700K–6500K, 800LM) — Wi-Fi 2.4GHz
- **No Thread Border Router needed** — Wi-Fi bulbs only
- **No RGB** — this project controls Kelvin and Brightness only

## Documentation

- [bridge/README.md](bridge/README.md) — Bridge setup and usage
- [docs/SETUP.md](docs/SETUP.md) — Full installation and pairing walkthrough
- [CLAUDE.md](CLAUDE.md) — Full design specification

## Author

Andrew Kuehne
