"""
test_sender.py — SimHub AmbiMatter UDP Test Sender

Simulates a full day/night cycle plus tunnel event via UDP packets,
so the Python bridge can be tested without a running SimHub instance.

All steps target a single zone and send JSON matching the bridge's
expected format: {"zone": "...", "kelvin": N, "brightness": N, "transition": N}

Usage:
    python test_sender.py [--zone ceiling] [--host 127.0.0.1] [--port 10001]
                         [--fast] [--pause]

Flags:
    --zone     Zone name to target (default: ceiling)
    --host     UDP destination host (default: 127.0.0.1)
    --port     UDP destination port (default: 10001)
    --fast     Use 1.5s sleeps instead of 3.0s (faster iteration)
    --pause    Require Enter key between steps (manual inspection mode)
"""

import argparse
import json
import socket
import time

# ---------------------------------------------------------------------------
# Day cycle definition
# Step format: (label, kelvin, brightness, transition, sleep_override_or_None)
#   transition: Matter units — 1=100ms, 8=800ms, 0=instant
#   sleep_override: seconds to wait after this step (None = use default sleep)
# ---------------------------------------------------------------------------

DAY_CYCLE = [
    # label                     kelvin  bright  trans  sleep
    ("Night start (baseline)",   2700,    20,     0,   None),  # instant baseline
    ("Pre-dawn",                 2750,    30,     8,   None),
    ("Dawn",                     2900,    60,     8,   None),
    ("Sunrise",                  3200,   100,     8,   None),
    ("Mid-morning",              4000,   160,     8,   None),
    ("Late morning",             5000,   210,     4,   None),
    ("Noon",                     6500,   254,     8,   None),
    ("Afternoon clouds",         5500,   180,     4,   None),
    ("Heavy overcast",           5800,   130,     4,   None),
    ("Cloud clear",              6000,   210,     8,   None),
    ("Late afternoon",           4500,   180,     8,   None),
    ("Sunset",                   3000,   120,     8,   None),
    ("Dusk",                     2800,    60,     8,   None),
    ("Night",                    2700,    25,     8,   None),
    ("TUNNEL ENTRY",             2700,     5,     0,   1.5),   # instant cut, short pause
    ("(in tunnel — no send)",    None,  None,  None,   1.5),   # pause only, no packet
    ("TUNNEL EXIT",              2700,    25,     0,   1.5),   # instant cut, short pause
    ("Return to night",          2700,    20,     8,   None),
]

SEPARATOR = "─" * 60


def send_step(
    sock: socket.socket,
    addr: tuple[str, int],
    zone: str,
    step_num: int,
    total: int,
    label: str,
    kelvin: int | None,
    brightness: int | None,
    transition: int | None,
    sleep_secs: float,
    pause: bool,
) -> None:
    """Send a single step packet (or skip if it's a pause-only step)."""
    print(f"\nStep {step_num:>2}/{total}  {label}")

    if kelvin is None:
        print("  (no packet — pause only)")
        _wait(sleep_secs, pause, label)
        return

    payload = {
        "zone": zone,
        "kelvin": kelvin,
        "brightness": brightness,
        "transition": transition,
    }
    data = json.dumps(payload).encode("utf-8")
    print(f"  Payload : {json.dumps(payload)}")

    sock.sendto(data, addr)
    print(f"  SENT → {addr[0]}:{addr[1]}")

    _wait(sleep_secs, pause, label)


def _wait(sleep_secs: float, pause: bool, label: str) -> None:
    if pause:
        input(f"  [Press Enter to continue from '{label}'] ")
    else:
        print(f"  Waiting {sleep_secs:.1f}s …")
        time.sleep(sleep_secs)


def run(args: argparse.Namespace) -> None:
    default_sleep = 1.5 if args.fast else 3.0
    addr = (args.host, args.port)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    # Count sendable steps (skip pause-only rows) for display
    total_steps = len(DAY_CYCLE)

    print(SEPARATOR)
    print("AmbiMatter Test Sender")
    print(f"  Target : {addr[0]}:{addr[1]}")
    print(f"  Zone   : {args.zone}")
    print(f"  Steps  : {total_steps}")
    print(f"  Sleep  : {default_sleep:.1f}s between steps {'(fast mode)' if args.fast else ''}")
    print(f"  Pause  : {'yes' if args.pause else 'no'}")
    print(SEPARATOR)

    try:
        for i, (label, kelvin, brightness, transition, sleep_override) in enumerate(DAY_CYCLE, 1):
            sleep_secs = sleep_override if sleep_override is not None else default_sleep
            send_step(
                sock=sock,
                addr=addr,
                zone=args.zone,
                step_num=i,
                total=total_steps,
                label=label,
                kelvin=kelvin,
                brightness=brightness,
                transition=transition,
                sleep_secs=sleep_secs,
                pause=args.pause,
            )

    except KeyboardInterrupt:
        print("\n\nAborted by user.")
    finally:
        sock.close()

    print(f"\n{SEPARATOR}")
    print("Test sequence complete.")
    print(SEPARATOR)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="AmbiMatter UDP test sender — simulates a full day/night/tunnel cycle"
    )
    parser.add_argument("--zone", default="ceiling", help="Zone name to target (default: ceiling)")
    parser.add_argument("--host", default="127.0.0.1", help="UDP host (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=10001, help="UDP port (default: 10001)")
    parser.add_argument(
        "--fast",
        action="store_true",
        help="Use 1.5s sleeps instead of 3.0s (faster iteration / dry-run testing)",
    )
    parser.add_argument(
        "--pause",
        action="store_true",
        help="Require Enter key between steps for manual inspection",
    )
    args = parser.parse_args()
    run(args)


if __name__ == "__main__":
    main()
