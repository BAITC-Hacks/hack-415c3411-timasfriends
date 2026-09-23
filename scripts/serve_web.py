#!/usr/bin/env python3
"""Serve a QuestBridge WebGL build and proxy its API using only Python's stdlib."""

from __future__ import annotations

import argparse
import functools
import http.client
import json
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit


REPOSITORY = Path(__file__).resolve().parents[1]
MAX_BODY_BYTES = 64 * 1024
HOP_HEADERS = {
    "connection", "keep-alive", "proxy-authenticate", "proxy-authorization",
    "te", "trailer", "transfer-encoding", "upgrade",
}


class QuestBridgeHandler(SimpleHTTPRequestHandler):
    server_version = "QuestBridgePreview/1.0"
    extensions_map = {
        **SimpleHTTPRequestHandler.extensions_map,
        ".wasm": "application/wasm",
        ".js": "application/javascript",
        ".mjs": "application/javascript",
        ".data": "application/octet-stream",
        ".unityweb": "application/octet-stream",
        ".json": "application/json",
        ".css": "text/css",
    }

    def __init__(self, *args, directory: str, backend: str, **kwargs):
        self.root = Path(directory).resolve()
        self.backend = urlsplit(backend)
        super().__init__(*args, directory=directory, **kwargs)

    def _is_api(self) -> bool:
        path = urlsplit(self.path).path
        return path == "/health" or path == "/api" or path.startswith("/api/")

    def do_GET(self):
        if self._is_api():
            self._proxy()
        else:
            super().do_GET()

    def do_HEAD(self):
        if self._is_api():
            self._proxy()
        else:
            super().do_HEAD()

    def _api_method(self):
        if self._is_api():
            self._proxy()
        else:
            self.send_error(HTTPStatus.METHOD_NOT_ALLOWED)

    do_POST = _api_method
    do_PATCH = _api_method
    do_PUT = _api_method
    do_DELETE = _api_method
    do_OPTIONS = _api_method

    def guess_type(self, path):
        # Also supports a later gzip/Brotli build with native browser decompression.
        suffix = Path(path).suffix.lower()
        if suffix in {".gz", ".br"}:
            path = str(Path(path).with_suffix(""))
        return super().guess_type(path)

    def send_head(self):
        resolved = Path(self.translate_path(self.path)).resolve()
        if resolved != self.root and self.root not in resolved.parents:
            self.send_error(HTTPStatus.FORBIDDEN)
            return None
        return super().send_head()

    def list_directory(self, path):
        self.send_error(HTTPStatus.NOT_FOUND, "No index.html in this directory")
        return None

    def send_response(self, code, message=None):
        self.response_status = code
        super().send_response(code, message)

    def end_headers(self):
        self.send_header("X-Content-Type-Options", "nosniff")
        if not self._is_api():
            # Revalidate fresh builds rather than pinning yesterday's loader.
            self.send_header("Cache-Control", "no-cache")
            suffix = Path(urlsplit(self.path).path).suffix.lower()
            if suffix == ".br" and self.response_status in {200, 304}:
                self.send_header("Content-Encoding", "br")
            elif suffix == ".gz" and self.response_status in {200, 304}:
                self.send_header("Content-Encoding", "gzip")
        super().end_headers()

    def _json_error(self, status: int, code: str, message: str):
        content = json.dumps({"error": {"code": code, "message": message}}, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(content)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(content)

    def _proxy(self):
        if self.headers.get("Transfer-Encoding"):
            self._json_error(HTTPStatus.LENGTH_REQUIRED, "length_required", "A Content-Length header is required.")
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self._json_error(HTTPStatus.BAD_REQUEST, "invalid_length", "Invalid Content-Length.")
            return
        if size < 0 or size > MAX_BODY_BYTES:
            self._json_error(HTTPStatus.REQUEST_ENTITY_TOO_LARGE, "request_too_large", "The request exceeds 64 KiB.")
            return

        body = self.rfile.read(size) if size else None
        connection_type = http.client.HTTPSConnection if self.backend.scheme == "https" else http.client.HTTPConnection
        connection = connection_type(self.backend.hostname, self.backend.port, timeout=35)
        request_url = urlsplit(self.path)
        target = self.backend.path.rstrip("/") + request_url.path
        if request_url.query:
            target += "?" + request_url.query
        excluded = HOP_HEADERS | {"host", "content-length", "accept-encoding"}
        excluded.update(part.strip().lower() for part in self.headers.get("Connection", "").split(","))
        headers = {name: value for name, value in self.headers.items() if name.lower() not in excluded}
        headers["Accept-Encoding"] = "identity"
        if body is not None:
            headers["Content-Length"] = str(len(body))

        try:
            connection.request(self.command, target, body=body, headers=headers)
            response = connection.getresponse()
            content = response.read()
        except (OSError, http.client.HTTPException):
            self._json_error(HTTPStatus.BAD_GATEWAY, "backend_unavailable", "Сервер задач недоступен. Запустите backend на порту 8000.")
            return
        finally:
            connection.close()

        self.send_response(response.status, response.reason)
        response_excluded = HOP_HEADERS | {"content-length", "server", "date"}
        response_excluded.update(part.strip().lower() for part in response.getheader("Connection", "").split(","))
        for name, value in response.getheaders():
            if name.lower() not in response_excluded:
                self.send_header(name, value)
        length = response.getheader("Content-Length") if self.command == "HEAD" else str(len(content))
        if length is not None:
            self.send_header("Content-Length", length)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(content)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=REPOSITORY / "Builds" / "WebGL", help="WebGL build folder containing index.html")
    parser.add_argument("--host", default="127.0.0.1", help="Bind address; use 0.0.0.0 only when a LAN demo is needed")
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--backend", default="http://127.0.0.1:8000", help="Fixed backend base URL; not selected by incoming requests")
    args = parser.parse_args()
    root = args.directory.resolve()
    if not (root / "index.html").is_file():
        parser.error(f"No index.html in {root}. Build QuestBridge WebGL first.")
    endpoint = urlsplit(args.backend)
    if endpoint.scheme not in {"http", "https"} or not endpoint.hostname or endpoint.username or endpoint.password or endpoint.query or endpoint.fragment:
        parser.error("--backend must be an HTTP(S) base URL without credentials, query or fragment.")
    try:
        endpoint.port
    except ValueError:
        parser.error("Invalid backend port.")
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535.")
    handler = functools.partial(QuestBridgeHandler, directory=str(root), backend=args.backend)
    server = ThreadingHTTPServer((args.host, args.port), handler)
    server.daemon_threads = True
    print(f"QuestBridge: http://{'localhost' if args.host in {'127.0.0.1', '0.0.0.0'} else args.host}:{args.port}/", flush=True)
    print(f"Build: {root}\nAPI proxy: {args.backend}\nStop with Ctrl+C.", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
