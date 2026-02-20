"""
ambient_bridge.py — SimHub AmbiMatter Python Matter Bridge

Receives UDP JSON commands from the SimHub C# plugin and forwards them
as Matter protocol commands to CCT smart bulbs organized in zones.

Usage:
    python ambient_bridge.py [--config config.yaml] [--dry-run] [--log-level INFO]
"""

import argparse
import asyncio
import json
import logging
import sys
from dataclasses import dataclass
from pathlib import Path

import yaml

logger = logging.getLogger("ambimatter")


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class ZoneConfig:
    name: str
    node_ids: list[int]
    endpoint_id: int
    brightness_multiplier: float
    min_kelvin: int
    max_kelvin: int


@dataclass
class ZoneState:
    """Tracks last-sent state for deduplication. -1 means never sent."""
    last_mireds: int = -1
    last_brightness: int = -1


@dataclass(frozen=True)
class BridgeConfig:
    matter_server_url: str
    udp_listen_ip: str
    udp_listen_port: int
    reconnect_initial_delay: float
    reconnect_max_delay: float
    reconnect_multiplier: float
    dedup_mireds_threshold: int
    dedup_brightness_threshold: int
    zones: dict[str, ZoneConfig]


@dataclass
class UdpCommand:
    zone: str
    kelvin: int
    brightness: int
    transition: int  # Matter units: 1=100ms, 8=800ms, 0=instant


# ---------------------------------------------------------------------------
# Config loading
# ---------------------------------------------------------------------------

def load_config(path: str) -> BridgeConfig:
    """Parse and validate config.yaml. Raises with clear messages on errors."""
    config_path = Path(path)
    if not config_path.exists():
        raise FileNotFoundError(f"Config file not found: {path}")

    with config_path.open("r", encoding="utf-8") as f:
        raw = yaml.safe_load(f)

    if not isinstance(raw, dict):
        raise ValueError(f"Config file is not a valid YAML mapping: {path}")

    def require(key: str, parent: dict, context: str = "config"):
        if key not in parent:
            raise ValueError(f"Missing required field '{key}' in {context}")
        return parent[key]

    zones_raw = require("zones", raw)
    if not isinstance(zones_raw, dict) or not zones_raw:
        raise ValueError("'zones' must be a non-empty mapping in config")

    zones: dict[str, ZoneConfig] = {}
    for zone_name, zone_data in zones_raw.items():
        if not isinstance(zone_data, dict):
            raise ValueError(f"Zone '{zone_name}' must be a mapping")
        node_ids = require("node_ids", zone_data, f"zone '{zone_name}'")
        if not isinstance(node_ids, list) or not node_ids:
            raise ValueError(f"Zone '{zone_name}': 'node_ids' must be a non-empty list")
        zones[zone_name] = ZoneConfig(
            name=zone_name,
            node_ids=[int(n) for n in node_ids],
            endpoint_id=int(zone_data.get("endpoint_id", 1)),
            brightness_multiplier=float(zone_data.get("brightness_multiplier", 1.0)),
            min_kelvin=int(zone_data.get("min_kelvin", 2700)),
            max_kelvin=int(zone_data.get("max_kelvin", 6500)),
        )

    return BridgeConfig(
        matter_server_url=raw.get("matter_server_url", "ws://localhost:5580/ws"),
        udp_listen_ip=raw.get("udp_listen_ip", "127.0.0.1"),
        udp_listen_port=int(raw.get("udp_listen_port", 10001)),
        reconnect_initial_delay=float(raw.get("reconnect_initial_delay", 2.0)),
        reconnect_max_delay=float(raw.get("reconnect_max_delay", 60.0)),
        reconnect_multiplier=float(raw.get("reconnect_multiplier", 2.0)),
        dedup_mireds_threshold=int(raw.get("dedup_mireds_threshold", 10)),
        dedup_brightness_threshold=int(raw.get("dedup_brightness_threshold", 5)),
        zones=zones,
    )


# ---------------------------------------------------------------------------
# Conversion utilities
# ---------------------------------------------------------------------------

def kelvin_to_mireds(kelvin: int, min_kelvin: int, max_kelvin: int) -> int:
    """Clamp kelvin to zone range, then convert to mireds (1,000,000 / kelvin)."""
    kelvin = max(min_kelvin, min(max_kelvin, kelvin))
    # min_kelvin is at least 2700 per config, so no ZeroDivisionError risk
    return int(1_000_000 / kelvin)


def clamp_brightness(raw: int, multiplier: float) -> int:
    """Apply zone brightness multiplier and clamp to Matter Level Control range 0-254."""
    return max(0, min(254, int(raw * multiplier)))


# ---------------------------------------------------------------------------
# UDP protocol (DatagramProtocol)
# ---------------------------------------------------------------------------

class UdpCommandProtocol(asyncio.DatagramProtocol):
    """Listens for incoming UDP JSON packets and enqueues parsed commands."""

    REQUIRED_FIELDS = {"zone", "kelvin", "brightness", "transition"}

    def __init__(self, queue: asyncio.Queue, known_zones: set[str]) -> None:
        self._queue = queue
        self._known_zones = known_zones

    def datagram_received(self, data: bytes, addr: tuple) -> None:
        logger.debug("UDP packet from %s:%d (%d bytes)", addr[0], addr[1], len(data))
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError:
            logger.warning("UDP packet from %s is not valid UTF-8 — discarding", addr)
            return

        try:
            payload = json.loads(text)
        except json.JSONDecodeError as exc:
            logger.warning("Malformed JSON from %s: %s — discarding", addr, exc)
            return

        if not isinstance(payload, dict):
            logger.warning("UDP payload is not a JSON object — discarding")
            return

        missing = self.REQUIRED_FIELDS - payload.keys()
        if missing:
            logger.warning("UDP packet missing fields %s — discarding", missing)
            return

        zone = str(payload["zone"])
        if zone not in self._known_zones:
            logger.warning(
                "Unknown zone '%s' (known: %s) — discarding",
                zone,
                sorted(self._known_zones),
            )
            return

        try:
            cmd = UdpCommand(
                zone=zone,
                kelvin=int(payload["kelvin"]),
                brightness=int(payload["brightness"]),
                transition=int(payload["transition"]),
            )
        except (TypeError, ValueError) as exc:
            logger.warning("UDP packet has invalid field values: %s — discarding", exc)
            return

        try:
            self._queue.put_nowait(cmd)
            logger.debug("Queued command: zone=%s kelvin=%d brightness=%d transition=%d",
                         cmd.zone, cmd.kelvin, cmd.brightness, cmd.transition)
        except asyncio.QueueFull:
            logger.warning("Command queue full — dropping oldest command to make room")
            try:
                self._queue.get_nowait()
            except asyncio.QueueEmpty:
                pass
            self._queue.put_nowait(cmd)

    def error_received(self, exc: Exception) -> None:
        logger.error("UDP error: %s", exc)


# ---------------------------------------------------------------------------
# Matter command dispatch
# ---------------------------------------------------------------------------

async def _send_to_bulb(
    client,
    node_id: int,
    endpoint_id: int,
    mireds: int,
    brightness: int,
    transition: int,
) -> None:
    """
    Send color temperature + brightness commands to a single Matter node.
    The chip.clusters import is lazy so --dry-run works without the CHIP SDK.
    """
    from chip.clusters import Objects as clusters  # noqa: PLC0415

    try:
        await client.send_device_command(
            node_id=node_id,
            endpoint_id=endpoint_id,
            command=clusters.ColorControl.Commands.MoveToColorTemperature(
                colorTemperatureMireds=mireds,
                transitionTime=transition,
                optionsMask=1,      # execute even if bulb reports it is "off"
                optionsOverride=1,
            ),
        )
        logger.debug("  node %d: color temperature OK (mireds=%d)", node_id, mireds)
    except Exception as exc:
        logger.error("  node %d: color temperature command failed: %s", node_id, exc)

    try:
        await client.send_device_command(
            node_id=node_id,
            endpoint_id=endpoint_id,
            command=clusters.LevelControl.Commands.MoveToLevelWithOnOff(
                level=brightness,
                transitionTime=transition,
                optionsMask=0,
                optionsOverride=0,
            ),
        )
        logger.debug("  node %d: brightness OK (level=%d)", node_id, brightness)
    except Exception as exc:
        logger.error("  node %d: brightness command failed: %s", node_id, exc)


async def send_zone_command(
    client,  # MatterClient or None in dry-run
    zone_cfg: ZoneConfig,
    zone_state: ZoneState,
    cmd: UdpCommand,
    config: BridgeConfig,
    dry_run: bool,
) -> None:
    """
    Process one zone command: apply multiplier, convert units, dedup, send.
    Updates zone_state on success (or dry-run).
    """
    brightness = clamp_brightness(cmd.brightness, zone_cfg.brightness_multiplier)
    mireds = kelvin_to_mireds(cmd.kelvin, zone_cfg.min_kelvin, zone_cfg.max_kelvin)
    transition_ms = cmd.transition * 100  # for human-readable logging only

    # Deduplication — skip if change is below both thresholds (and not first send)
    if zone_state.last_mireds != -1:
        mireds_delta = abs(mireds - zone_state.last_mireds)
        brightness_delta = abs(brightness - zone_state.last_brightness)
        if (mireds_delta < config.dedup_mireds_threshold
                and brightness_delta < config.dedup_brightness_threshold):
            logger.debug(
                "Dedup skip zone=%s (Δmireds=%d, Δbrightness=%d)",
                cmd.zone, mireds_delta, brightness_delta,
            )
            return

    logger.info(
        "Zone %-12s  mireds=%-5d  brightness=%-3d  transition=%dms%s",
        cmd.zone, mireds, brightness, transition_ms,
        "  [DRY-RUN]" if dry_run else "",
    )

    if dry_run:
        print(
            f"[DRY-RUN] zone={cmd.zone}  kelvin={cmd.kelvin}K → mireds={mireds}"
            f"  brightness={cmd.brightness} → {brightness}"
            f"  transition={transition_ms}ms"
            f"  nodes={zone_cfg.node_ids}"
        )
        zone_state.last_mireds = mireds
        zone_state.last_brightness = brightness
        return

    # Send to each bulb sequentially (protects ESP32-C3 network buffers)
    for node_id in zone_cfg.node_ids:
        await _send_to_bulb(
            client, node_id, zone_cfg.endpoint_id,
            mireds, brightness, cmd.transition,
        )

    zone_state.last_mireds = mireds
    zone_state.last_brightness = brightness


# ---------------------------------------------------------------------------
# Command dispatch loop
# ---------------------------------------------------------------------------

async def _command_loop(
    client,
    config: BridgeConfig,
    zone_states: dict[str, ZoneState],
    queue: asyncio.Queue,
    dry_run: bool = False,
) -> None:
    """Pull commands from the queue and dispatch them indefinitely."""
    while True:
        cmd: UdpCommand = await queue.get()
        zone_cfg = config.zones.get(cmd.zone)
        if zone_cfg is None:
            # Should not happen — validated in UdpCommandProtocol — but be safe
            logger.warning("Received command for unknown zone '%s' — skipping", cmd.zone)
            continue
        await send_zone_command(
            client, zone_cfg, zone_states[cmd.zone], cmd, config, dry_run
        )


def _drain_queue(queue: asyncio.Queue) -> int:
    """Discard all pending commands from the queue. Returns count drained."""
    count = 0
    while not queue.empty():
        try:
            queue.get_nowait()
            count += 1
        except asyncio.QueueEmpty:
            break
    if count:
        logger.info("Drained %d stale command(s) from queue after reconnect", count)
    return count


# ---------------------------------------------------------------------------
# Matter reconnect loop
# ---------------------------------------------------------------------------

async def _matter_reconnect_loop(
    config: BridgeConfig,
    zone_states: dict[str, ZoneState],
    queue: asyncio.Queue,
) -> None:
    """
    Connect to the Matter server with exponential backoff on failure.
    Zone states persist across reconnects so deduplication remains accurate.
    """
    from matter_server.client import MatterClient
    from matter_server.client.exceptions import (
        CannotConnect,
        ConnectionFailed,
        InvalidServerVersion,
    )
    import aiohttp

    delay = config.reconnect_initial_delay

    while True:
        try:
            logger.info("Connecting to Matter server at %s …", config.matter_server_url)
            async with aiohttp.ClientSession() as session:
                async with MatterClient(config.matter_server_url, session) as client:
                    delay = config.reconnect_initial_delay  # reset on success
                    logger.info("Matter server connected.")
                    _drain_queue(queue)

                    listen_task = asyncio.create_task(client.start_listening())
                    try:
                        await _command_loop(client, config, zone_states, queue)
                    finally:
                        listen_task.cancel()
                        try:
                            await listen_task
                        except (asyncio.CancelledError, Exception):
                            pass

        except (CannotConnect, ConnectionFailed) as exc:
            logger.warning(
                "Matter server connection failed: %s — retrying in %.0fs", exc, delay
            )
            await asyncio.sleep(delay)
            delay = min(delay * config.reconnect_multiplier, config.reconnect_max_delay)

        except ConnectionResetError as exc:
            logger.warning(
                "Matter connection dropped: %s — retrying in %.0fs", exc, delay
            )
            await asyncio.sleep(delay)
            delay = min(delay * config.reconnect_multiplier, config.reconnect_max_delay)

        except InvalidServerVersion as exc:
            logger.critical(
                "Matter server version incompatible: %s — cannot continue", exc
            )
            raise SystemExit(1) from exc

        except asyncio.CancelledError:
            raise  # propagate clean shutdown


# ---------------------------------------------------------------------------
# Dry-run mode (no Matter server needed)
# ---------------------------------------------------------------------------

async def _dry_run_loop(
    config: BridgeConfig,
    zone_states: dict[str, ZoneState],
    queue: asyncio.Queue,
) -> None:
    """Command loop for --dry-run: no Matter client, just print to stdout."""
    logger.info("Dry-run mode active — commands will be printed, not sent to Matter.")
    await _command_loop(None, config, zone_states, queue, dry_run=True)


# ---------------------------------------------------------------------------
# Bridge entry point
# ---------------------------------------------------------------------------

async def run_bridge(config: BridgeConfig, dry_run: bool) -> None:
    """Set up the UDP listener and start the main loop."""
    zone_states: dict[str, ZoneState] = {name: ZoneState() for name in config.zones}
    queue: asyncio.Queue[UdpCommand] = asyncio.Queue(maxsize=100)

    loop = asyncio.get_running_loop()

    logger.info("Binding UDP listener on %s:%d", config.udp_listen_ip, config.udp_listen_port)
    transport, _ = await loop.create_datagram_endpoint(
        lambda: UdpCommandProtocol(queue, set(config.zones.keys())),
        local_addr=(config.udp_listen_ip, config.udp_listen_port),
    )
    logger.info(
        "UDP listener ready. Zones: %s",
        ", ".join(
            f"{name} ({len(z.node_ids)} bulb{'s' if len(z.node_ids) != 1 else ''})"
            for name, z in config.zones.items()
        ),
    )

    try:
        if dry_run:
            await _dry_run_loop(config, zone_states, queue)
        else:
            await _matter_reconnect_loop(config, zone_states, queue)
    finally:
        transport.close()
        logger.info("UDP transport closed.")


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="AmbiMatter Python Matter Bridge — routes SimHub UDP commands to Matter bulbs"
    )
    parser.add_argument(
        "--config",
        default="config.yaml",
        help="Path to config YAML file (default: config.yaml)",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print commands to stdout instead of sending to Matter server",
    )
    parser.add_argument(
        "--log-level",
        default="INFO",
        choices=["DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"],
        help="Logging verbosity (default: INFO)",
    )
    return parser.parse_args()


def main() -> None:
    args = _parse_args()

    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(asctime)s  %(levelname)-8s  %(name)s  %(message)s",
        datefmt="%H:%M:%S",
    )

    try:
        config = load_config(args.config)
    except (FileNotFoundError, ValueError) as exc:
        logger.critical("Configuration error: %s", exc)
        sys.exit(1)

    logger.info("AmbiMatter bridge starting (dry-run=%s)", args.dry_run)
    logger.info("Config: %s", args.config)

    try:
        asyncio.run(run_bridge(config, dry_run=args.dry_run))
    except KeyboardInterrupt:
        logger.info("Shutdown requested — exiting.")
    except SystemExit:
        raise
    except Exception as exc:
        logger.critical("Unhandled exception: %s", exc, exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    main()
