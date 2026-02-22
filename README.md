# SimHub AmbiMatter
This is the first app i have made using claude code.

Ambient room lighting that reacts to your screen content via Matter-compatible
CCT smart bulbs. Works with any game — the plugin captures your monitor and
derives color temperature and brightness from the on-screen image.

## How It Works

```
Screen Capture → C# Plugin → UDP → Python Bridge → Matter → Smart Bulbs
```

1. The **C# SimHub plugin** captures the game screen, computes a target color
   temperature (Kelvin) and brightness using McCamy's CCT formula on the
   average pixel color, and sends UDP packets to the bridge.
2. The **Python Matter bridge** receives those packets and sends Matter protocol
   commands to your bulbs, organized into named zones.

The plugin uses proper color science (sRGB → XYZ → chromaticity → CCT) with
gamma correction and EMA smoothing to produce stable, accurate ambient lighting.
The system uses CCT (Kelvin + Brightness) only — no RGB. Bulbs smoothly
transition between states using Matter's built-in transition time, keeping
command frequency low enough to avoid crashing Wi-Fi Matter devices.

## Status

| Component | Status |
|---|---|
| Python Matter Bridge | complete |
| C# SimHub Plugin (screen capture) | complete |
| Guillotine tunnel detection | complete |

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
