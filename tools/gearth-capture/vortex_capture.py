"""Records a G-Earth session into the capture format Vortex.Specs already imports.

    python -m pip install g-python
    python vortex_capture.py --selftest     # no G-Earth needed
    python vortex_capture.py -p 9092        # or add the file to G-Earth's extension list

Double-click the extension to start recording, double-click again to write the file into
docs/habbo-specs/evidence/captures/. Losing the connection writes it too.

The recorder is deliberately dumb: packets in, JSON out. Every judgement about what a capture
means belongs in Vortex.Specs, which already imports this schema, splits it into trigger
observations and diffs two of them against each other. Nothing here decides anything.

Header ids are per-revision, and the workspace can only name 48 of AIR-1.0.31's, so the name
G-Earth resolved is what makes a capture usable. It is written whenever G-Earth knows it.
"""

import json
import os
import sys
import time
from datetime import datetime, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(HERE, "capture.config.json")
OUT_DIR = os.path.normpath(
    os.path.join(HERE, "..", "..", "docs", "habbo-specs", "evidence", "captures")
)

EXTENSION_INFO = {
    "title": "Vortex Capture",
    "description": "Records a session into docs/habbo-specs/evidence/captures/",
    "version": "1.0",
    "author": "vortex-cloud",
}


def load_config():
    """Read the capture header. Re-read per recording, so it can be edited between two."""
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except FileNotFoundError:
        return {}


def output_path(capture_id):
    """First free name. A recording is never silently written over a previous one."""
    candidate = os.path.join(OUT_DIR, capture_id + ".json")
    suffix = 2
    while os.path.exists(candidate):
        candidate = os.path.join(OUT_DIR, "{}-{:03d}.json".format(capture_id, suffix))
        suffix += 1
    return candidate


class Recorder:
    def __init__(self, direction_name, resolve_name):
        self._direction_name = direction_name
        self._resolve_name = resolve_name
        self.messages = []
        self.recording = False
        self._started_at = None

    def toggle(self):
        if self.recording:
            self.recording = False
            return self.write()
        self.messages = []
        self._started_at = None
        self.recording = True
        print("recording")
        return None

    def on_message(self, message):
        if not self.recording:
            return

        now = time.time()
        if self._started_at is None:
            self._started_at = now

        packet = message.packet
        header = packet.header_id()
        name = self._resolve_name(message.direction, header)

        entry = {"index": len(self.messages), "direction": self._direction_name(message.direction)}
        if name:
            entry["name"] = name
        entry["header"] = header
        entry["timestampMs"] = int((now - self._started_at) * 1000)
        # Body only: the 6-byte prefix is the length and the header id, both already above.
        entry["payloadHex"] = bytes(packet.bytearray[6:]).hex()

        self.messages.append(entry)

    def document(self, config):
        document = {
            "id": config.get("id") or datetime.now(timezone.utc).strftime("capture-%Y%m%d-%H%M%S"),
            # 'unknown' is not a bug: the importer treats an unstated source as unbacked and says
            # so, which is the honest default for a file nobody has vouched for yet.
            "source": config.get("source") or "unknown",
        }
        for key in ("revision", "note"):
            if config.get(key):
                document[key] = config[key]
        document["recordedUtc"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        document["messages"] = self.messages
        return document

    def write(self):
        if not self.messages:
            print("nothing recorded")
            return None

        document = self.document(load_config())
        os.makedirs(OUT_DIR, exist_ok=True)
        path = output_path(document["id"])

        with open(path, "w", encoding="utf-8") as handle:
            json.dump(document, handle, indent=2)
            handle.write("\n")

        print("wrote {} ({} messages)".format(path, len(self.messages)))
        return path


def main():
    from g_python.gextension import Extension
    from g_python.hmessage import Direction

    extension = Extension(EXTENSION_INFO, sys.argv)

    def resolve_name(direction, header):
        for info in extension.packet_infos.get(direction, {}).get(header, []):
            if info.get("Name"):
                return info["Name"]
        return None

    recorder = Recorder(
        lambda direction: "client_to_server"
        if direction == Direction.TO_SERVER
        else "server_to_client",
        resolve_name,
    )

    extension.intercept(Direction.TO_CLIENT, recorder.on_message)
    extension.intercept(Direction.TO_SERVER, recorder.on_message)
    extension.on_event("double_click", recorder.toggle)
    extension.on_event("connection_end", lambda: recorder.recording and recorder.toggle())
    extension.start()


def selftest():
    """The whole point is that Vortex.Specs can read the result, so assert the shape it reads."""

    class FakePacket:
        def __init__(self, header, body):
            self.bytearray = bytearray(b"\x00\x00\x00\x00\x00\x00") + body
            self._header = header

        def header_id(self):
            return self._header

    class FakeMessage:
        def __init__(self, direction, packet):
            self.direction = direction
            self.packet = packet

    names = {("to_server", 4000): "ClientHello"}
    recorder = Recorder(
        lambda direction: "client_to_server" if direction == "to_server" else "server_to_client",
        lambda direction, header: names.get((direction, header)),
    )

    recorder.recording = True
    recorder.on_message(FakeMessage("to_server", FakePacket(4000, b"\x01\x02")))
    recorder.on_message(FakeMessage("to_client", FakePacket(731, b"")))

    document = recorder.document({"id": "selftest", "source": "official", "revision": "AIR-1.0.31"})
    first, second = document["messages"]

    assert document["source"] == "official", document
    assert first["direction"] == "client_to_server", first
    assert second["direction"] == "server_to_client", second
    assert first["name"] == "ClientHello", first
    assert "name" not in second, "an unresolved id must not be given a made-up name"
    assert first["header"] == 4000 and second["header"] == 731, document
    assert first["payloadHex"] == "0102", first
    assert second["payloadHex"] == "", second
    assert [m["index"] for m in document["messages"]] == [0, 1], document

    print("selftest ok")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        selftest()
    else:
        main()
