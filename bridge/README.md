# AmbiMatter Python Matter Bridge

Receives UDP JSON commands from the SimHub C# plugin and sends them as Matter
protocol commands to CCT smart bulbs organized in configurable zones.

---

## Requirements

- Python 3.10+
- A running [python-matter-server](https://github.com/home-assistant-libs/python-matter-server) instance
- Bulbs paired to the Matter server (see `docs/SETUP.md`)

## Installation

```bash
cd bridge
pip install -r requirements.txt
```

## Configuration

Edit `config.yaml` to match your setup:

```yaml
matter_server_url: "ws://localhost:5580/ws"
udp_listen_port: 10001

zones:
  ceiling:
    node_ids: [1, 2, 3]   # Node IDs assigned when you paired the bulbs
    brightness_multiplier: 1.0
    min_kelvin: 2700
    max_kelvin: 6500
```

Set `node_ids` to the IDs assigned when you paired each bulb (visible in the
Matter server logs or companion app during pairing).

## Running

**Normal mode** (requires Matter server + paired bulbs):
```bash
python ambient_bridge.py
```

**Dry-run mode** (no hardware needed — prints commands to stdout):
```bash
python ambient_bridge.py --dry-run
```

**With verbose logging:**
```bash
python ambient_bridge.py --dry-run --log-level DEBUG
```

## Testing Without SimHub

Use `test_sender.py` to simulate a full day/night/tunnel cycle:

```bash
# Terminal 1 — start the bridge in dry-run
python ambient_bridge.py --dry-run --log-level DEBUG

# Terminal 2 — run the test sequence (fast mode for quick iteration)
python test_sender.py --fast
```

The test sender sends 18 UDP steps: night baseline → dawn → noon → sunset →
night → tunnel entry → tunnel exit → back to night.

```bash
# Manual step-through for detailed inspection
python test_sender.py --pause

# Target a different zone
python test_sender.py --zone desk_lamp --fast
```

## UDP Command Format

The bridge listens for JSON packets on `127.0.0.1:10001`:

```json
{"zone": "ceiling", "kelvin": 4500, "brightness": 200, "transition": 8}
```

| Field | Type | Description |
|---|---|---|
| `zone` | string | Zone name (must match a zone in config.yaml) |
| `kelvin` | int | Color temperature in Kelvin (2700–6500) |
| `brightness` | int | Raw brightness level 0–254 |
| `transition` | int | Matter transition units (1 = 100ms, 8 = 800ms, 0 = instant) |

The zone's `brightness_multiplier` is applied inside the bridge before sending.

## Reconnection Behavior

If the Matter server is unavailable at startup or drops mid-run, the bridge
retries with exponential backoff (default: 2s → 4s → 8s … up to 60s). Stale
commands queued during the outage are discarded on reconnect to avoid replaying
old state.

## Deduplication

Commands are deduplicated per-zone. A command is skipped if the mireds change
is below `dedup_mireds_threshold` (default 10) AND the brightness change is
below `dedup_brightness_threshold` (default 5). Adjust these in `config.yaml`
if bulbs flicker (lower thresholds) or miss updates (raise them).
