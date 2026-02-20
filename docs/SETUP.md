# AmbiMatter — Full Setup Guide

This guide covers installing `python-matter-server`, pairing your Linkind bulbs,
and configuring the AmbiMatter bridge.

---

## Prerequisites

- Windows 10/11 or Linux host
- Python 3.10 or later
- Your Linkind bulbs powered on and in pairing mode (factory-reset if needed)
- The AmbiMatter repo cloned locally

---

## Step 1 — Install python-matter-server

`python-matter-server` is the open-source Matter SDK wrapper used by Home
Assistant. It runs as a standalone WebSocket service.

```bash
pip install python-matter-server
```

Or in an isolated environment (recommended):

```bash
python -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install python-matter-server
```

### Start the Matter server

```bash
python -m matter_server.server --storage-path ./matter-data --port 5580
```

Keep this running in a dedicated terminal. The server persists paired device
data in `./matter-data` — don't delete that directory between runs.

---

## Step 2 — Install the Bridge

```bash
cd bridge
pip install -r requirements.txt
```

---

## Step 3 — Pair Your Bulbs

Each Linkind bulb has a QR code and pairing code printed on the label.

### Factory reset

If the bulb was previously paired, factory-reset it first:
- Toggle power: off → on → off → on → off → on (three quick off/on cycles)
- The bulb will blink to confirm reset

### Pair via python-matter-server CLI

With the Matter server running:

```bash
python -m matter_server.client --host localhost --port 5580 commission <PAIRING-CODE>
```

Replace `<PAIRING-CODE>` with the numeric code on the bulb's label.

After pairing succeeds, note the **Node ID** assigned to each bulb. The server
will log something like:

```
Node 1 commissioned successfully.
```

Pair all bulbs one at a time. Each gets its own Node ID (1, 2, 3, …).

### Multi-Admin (bulb already paired to Alexa/Google)

If your bulbs are already paired to a voice assistant, you can use Matter's
Multi-Admin feature to add the Matter server as a second controller without
removing the existing pairing. Follow your voice assistant app's instructions
for sharing a device via Matter.

---

## Step 4 — Configure the Bridge

Edit `bridge/config.yaml`:

```yaml
matter_server_url: "ws://localhost:5580/ws"
udp_listen_ip: "127.0.0.1"
udp_listen_port: 10001

zones:
  ceiling:
    node_ids: [1, 2, 3]   # ← replace with your actual Node IDs
    endpoint_id: 1
    brightness_multiplier: 1.0
    min_kelvin: 2700
    max_kelvin: 6500
```

Set `node_ids` to the Node IDs assigned during pairing. The order of IDs
within a zone does not matter — all bulbs in a zone receive the same command.

---

## Step 5 — Verify Without Hardware (Dry-Run)

Before connecting to real bulbs, verify the bridge parses commands correctly:

```bash
# Terminal 1
cd bridge
python ambient_bridge.py --dry-run --log-level DEBUG

# Terminal 2
python test_sender.py --fast
```

You should see 18 steps printed in Terminal 1, with `[DRY-RUN]` on each line.
Tunnel entry/exit steps will show `transition=0ms` (instant).

---

## Step 6 — Test With Real Bulbs

With the Matter server running and bulbs paired:

```bash
# Terminal 1 — start the bridge
cd bridge
python ambient_bridge.py --log-level INFO

# Terminal 2 — run the test sequence (3s between steps)
python test_sender.py
```

Watch the bulbs:
- Steps 1–14: gradual warm-to-cool shift (night → dawn → noon → sunset → night)
- Step 15 (TUNNEL ENTRY): instant drop to near-dark
- Step 17 (TUNNEL EXIT): instant return to night level
- Step 18: smooth 800ms fade back to final night state

---

## Tuning

### Bulbs are flickering / updating too often

Increase the dedup thresholds in `config.yaml`:

```yaml
dedup_mireds_threshold: 15      # was 10
dedup_brightness_threshold: 8   # was 5
```

### Bulbs are missing updates / changes look jerky

Lower the dedup thresholds:

```yaml
dedup_mireds_threshold: 5
dedup_brightness_threshold: 3
```

### Bulbs disconnecting or lagging

The Matter server command rate may be too high. The SimHub plugin (Phase 2)
enforces a 1500ms per-zone cooldown. If you are seeing disconnects in testing,
increase the sleep between test_sender.py steps or reduce the number of bulbs
per zone.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Bridge can't connect to Matter server | Server not running | Start `python -m matter_server.server` |
| "Unknown zone" warning | Zone name mismatch | Check zone names match between config.yaml and UDP packets |
| Bulb doesn't respond | Wrong Node ID | Re-check IDs from pairing logs |
| Bulb responds slowly | High network load | Reduce test frequency; check Wi-Fi 2.4GHz congestion |
| `InvalidServerVersion` error | SDK version mismatch | `pip install --upgrade python-matter-server` |
| Bridge crashes on import | Missing dependencies | Run `pip install -r requirements.txt` in the bridge directory |

---

## Running as a Service (Optional)

For continuous use alongside SimHub, you can run both the Matter server and
the bridge as background services. On Windows, consider using NSSM or a
scheduled task. On Linux, use systemd unit files.

The Matter server must be running before the bridge starts. The bridge will
retry automatically if the server is temporarily unavailable.
